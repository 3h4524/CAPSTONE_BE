import { mkdtemp, rm, stat, writeFile } from "node:fs/promises";
import { createReadStream } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { exportPayloadSchema, renderPayloadSchema, workerJobSchema } from "./contracts";
import type { RenderProps, WorkerJob } from "./contracts";
import { ApiError, failureCode } from "./failures";
import { createZip, downloadAssets, pngSource, uploadFile } from "./media-files";
import { RemotionVideoRenderer } from "./renderer";
import { packageBatchExport, type ExportProduct } from "./batch-export";

const baseUrl = new URL(process.env.APCS_API_URL ?? "http://host.docker.internal:5191");
const key = process.env.MEDIA_WORKER_API_KEY ?? "";
if (key.length < 32 || key.length > 256) throw new Error("Set MEDIA_WORKER_API_KEY (32–256 characters)");
if (baseUrl.protocol !== "https:" && !["localhost", "127.0.0.1", "host.docker.internal", "api"].includes(baseUrl.hostname)) throw new Error("Worker requires HTTPS outside a local/container network");
const renderer = new RemotionVideoRenderer();
const shutdown = new AbortController();
process.once("SIGINT", () => shutdown.abort()); process.once("SIGTERM", () => shutdown.abort());
// Submit one paid AI video request at a time by default. Some accounts/models become unstable when
// multiple long-running generations are started together; operators can still opt into 2-4.
const configuredAiConcurrency = Number(process.env.AI_SCENE_CONCURRENCY ?? "1");
const aiSceneConcurrency = Number.isInteger(configuredAiConcurrency) && configuredAiConcurrency >= 1
  ? Math.min(configuredAiConcurrency, 4)
  : 1;
const internalApiTimeoutMs = 20_000;
// A short API restart should not cancel a render that may already have spent minutes generating
// paid AI clips. Five failed heartbeats at 15-second intervals still stop the job before its
// two-minute lease can be reclaimed by another worker.
const heartbeatTimeoutMs = 8_000;
const heartbeatFailureLimit = 5;
// Starting an AI video scene waits for the API to submit the paid request and persist its operation name.
// The API provider timeout is three minutes, so the worker must not cancel that request after 20 seconds.
const aiShotStartTimeoutMs = 4 * 60_000;
async function api(path: string, payload: unknown, signal: AbortSignal, timeoutMs = internalApiTimeoutMs) {
  const response = await fetch(new URL(`/internal/media-jobs/${path}`, baseUrl), { method: "POST", headers: { "Content-Type": "application/json", "X-Media-Worker-Key": key }, body: JSON.stringify(payload), signal: AbortSignal.any([signal, AbortSignal.timeout(timeoutMs)]) });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { code?: unknown } | null;
    throw new ApiError(response.status, typeof problem?.code === "string" ? problem.code : null);
  }
  return response.status === 204 ? null : response.json();
}
async function aiShotBytes(job: WorkerJob, order: number, signal: AbortSignal) {
  const response = await fetch(new URL(`/internal/media-jobs/${job.id}/ai-shots/${order}/download`, baseUrl), {
    method: "POST", headers: { "Content-Type": "application/json", "X-Media-Worker-Key": key },
    body: JSON.stringify({ leaseToken: job.leaseToken }), signal: AbortSignal.any([signal, AbortSignal.timeout(120_000)])
  });
  if (!response.ok) throw new Error(`AI Shot download failed (${response.status})`);
  const bytes = Buffer.from(await response.arrayBuffer());
  if (!bytes.length || bytes.length > 100 * 1024 * 1024) throw new Error("Invalid AI Shot clip size");
  return bytes;
}
async function uploadLocalPackage(job: WorkerJob, file: string, signal: AbortSignal) {
  const response = await fetch(new URL(`/internal/media-jobs/${job.id}/package`, baseUrl), {
    method: "POST",
    headers: { "Content-Type": "application/zip", "X-Media-Worker-Key": key, "X-Media-Job-Lease": job.leaseToken },
    body: createReadStream(file), duplex: "half", signal
  } as unknown as RequestInit & { duplex: "half" });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { code?: unknown } | null;
    throw new ApiError(response.status, typeof problem?.code === "string" ? problem.code : "LocalExportUploadFailed");
  }
  const stored = await response.json() as { key?: unknown; sha256?: unknown; bytes?: unknown };
  const expectedKey = `local-export/${job.id}/${job.leaseToken}`;
  if (stored.key !== expectedKey || typeof stored.sha256 !== "string" || !/^[a-f0-9]{64}$/.test(stored.sha256) ||
      typeof stored.bytes !== "number" || stored.bytes !== (await stat(file)).size)
    throw new Error("API returned invalid local ZIP verification data");
  return stored as { key: string; sha256: string; bytes: number };
}
async function execute(job: WorkerJob) {
  const directory = await mkdtemp(join(tmpdir(), "apcs-media-"));
  const abort = new AbortController();
  const signal = AbortSignal.any([abort.signal, shutdown.signal, AbortSignal.timeout(30 * 60_000)]);
  let stage = "Planning", progress = 5, duringAiShot = false;
  let heartbeatRunning = false;
  let consecutiveHeartbeatFailures = 0;
  const heartbeat = setInterval(() => {
    if (heartbeatRunning) return;
    heartbeatRunning = true;
    void api(`${job.id}/heartbeat`, { leaseToken: job.leaseToken, stage, progress }, signal, heartbeatTimeoutMs)
      .then(() => { consecutiveHeartbeatFailures = 0; })
      .catch(error => {
        if (signal.aborted) return;
        consecutiveHeartbeatFailures++;
        process.stderr.write(
          `Media job ${job.id} heartbeat failed (${consecutiveHeartbeatFailures}/${heartbeatFailureLimit}): ${error instanceof Error ? error.message : "unknown error"}.\n`
        );
        if (consecutiveHeartbeatFailures >= heartbeatFailureLimit) abort.abort();
      })
      .finally(() => { heartbeatRunning = false; });
  }, 15_000);
  try {
    stage = "Generating Assets"; progress = 10;
    const assets = await downloadAssets(job, directory, signal);
    if (job.kind === "render") {
      const payload = renderPayloadSchema.parse(job.payload);
      const sources: RenderProps["sources"] = {};
      const clips: NonNullable<RenderProps["clips"]> = {};
      for (const scene of payload.storyboard.scenes) {
        const asset = assets.get(`mockups/${scene.mockupId}.png`);
        if (!asset) throw new Error("Missing source mockup");
        sources[scene.mockupId] = pngSource(asset.data, scene.sourceHash);
      }
      const aiShots = payload.storyboard.aiShots ?? [];
      const music = payload.storyboard.videoEdit?.music && !payload.storyboard.videoEdit.music.muted
        ? assets.get(`music/${payload.storyboard.videoEdit.music.trackId}.mp3`)?.file
        : undefined;
      if (payload.storyboard.videoEdit?.music && !payload.storyboard.videoEdit.music.muted && !music)
        throw new Error("Selected music track is unavailable");
      const sceneErrors: unknown[] = [];
      let nextShot = 0;
      let completedShots = 0;
      let stopScheduling = false;
      const generateShot = async (shot: NonNullable<typeof payload.storyboard.aiShots>[number]) => {
        const stored = assets.get(`ai-shots/${shot.sceneOrder}.mp4`);
        if (stored) { clips[shot.sceneOrder] = stored.file; return; }
        stage = "Generating Assets"; progress = Math.max(progress, 15); duringAiShot = true;
        const request = { leaseToken: job.leaseToken };
        let state: { status: string };
        try {
          state = await api(`${job.id}/ai-shots/${shot.sceneOrder}/start`, request, signal, aiShotStartTimeoutMs) as { status: string };
        } catch (error) {
          // The API may have submitted the paid request before this worker timed out. Never classify that
          // as a normal scene failure, which would make the queue submit it again automatically.
          if (error instanceof DOMException && error.name === "TimeoutError") throw new ApiError(409, "AiVideoSubmissionUncertain");
          throw error;
        }
        for (let check = 0; check < 240 && (state.status === "polling" || state.status === "pending"); check++) {
          // "pending" again: the provider dropped the clip without charging and the API allows one more submission.
          if (state.status === "pending") { state = await api(`${job.id}/ai-shots/${shot.sceneOrder}/start`, request, signal) as { status: string }; continue; }
          await delay(5_000, undefined, { signal });
          state = await api(`${job.id}/ai-shots/${shot.sceneOrder}/poll`, request, signal) as { status: string };
        }
        if (state.status !== "ready") throw new Error(`AI Shot scene ${shot.sceneOrder + 1} did not finish`);
        const file = join(directory, `ai-shot-${shot.sceneOrder}.mp4`);
        await writeFile(file, await aiShotBytes(job, shot.sceneOrder, signal));
        const grant = job.uploads[`ai-shot-${shot.sceneOrder}`];
        if (!grant) throw new Error("Missing AI Shot upload grant");
        const result = await uploadFile(grant, file, signal);
        await api(`${job.id}/ai-shots/${shot.sceneOrder}/stored`, { ...request, ...result }, signal);
        clips[shot.sceneOrder] = file;
      };
      await Promise.all(Array.from({ length: Math.min(aiSceneConcurrency, aiShots.length) }, async () => {
        while (true) {
          if (stopScheduling) return;
          const index = nextShot++;
          if (index >= aiShots.length) return;
          try {
            await generateShot(aiShots[index]!);
            completedShots++;
            progress = Math.max(progress, 15 + Math.round(20 * completedShots / aiShots.length));
          }
          catch (error) {
            stopScheduling = true;
            sceneErrors.push(error);
            process.stderr.write(`Media job ${job.id} AI scene ${aiShots[index]!.sceneOrder + 1} failed: ${error instanceof Error ? error.message : "unknown error"}.\n`);
            return;
          }
        }
      }));
      if (sceneErrors.length > 0) throw sceneErrors[0];
      duringAiShot = false;
      stage = "Rendering";
      const result = await renderer.render({ storyboard: payload.storyboard, sources, clips, music }, directory, signal, p => { progress = Math.max(progress, p); if (p >= 85) stage = "Technical QA"; });
      stage = "Uploading"; progress = 90;
      if (!job.uploads.video || !job.uploads.thumbnail) throw new Error("Missing upload grants");
      const video = await uploadFile(job.uploads.video, result.video, signal);
      const thumbnail = await uploadFile(job.uploads.thumbnail, result.thumbnail, signal);
      await api(`${job.id}/complete`, { leaseToken: job.leaseToken, ...video, thumbnailStorageKey: thumbnail.storageKey, thumbnailStorageVersion: thumbnail.storageVersion, qa: result.qa }, signal);
    } else {
      const payload = exportPayloadSchema.parse(job.payload);
      stage = "Exporting"; progress = 40;
      let zip = join(directory, "package.zip");
      if (payload.storyboard && payload.productName) {
        const outputImages: ExportProduct["images"] = [];
        for (const [index, mockup] of [...(payload.approvedMockups ?? [])].sort((a, b) => a.id.localeCompare(b.id)).entries()) {
          const source = assets.get(`mockups/${mockup.id}.png`);
          if (!source || !mockup.sha256) throw new Error("An approved mockup disappeared during ZIP export");
          pngSource(source.data, mockup.sha256);
          outputImages.push({ id: mockup.id, path: `mockup/mockup-${String(index + 1).padStart(2, "0")}.png`, file: source.file, version: mockup.storageVersion, sha256: mockup.sha256 });
        }
        for (const [index, design] of [...(payload.designs ?? [])].sort((a, b) => a.id.localeCompare(b.id)).entries()) {
          const source = assets.get(`designs/${design.id}.png`);
          if (!source) throw new Error("An approved design disappeared during ZIP export");
          outputImages.push({ id: design.id, path: `design/design-${String(index + 1).padStart(2, "0")}.png`, file: source.file });
        }
        const sourceSnapshot = payload.sources;
        const scenesReady = Boolean(sourceSnapshot && sourceSnapshot.videoId === payload.videoId &&
          sourceSnapshot.storyboardFingerprint === payload.storyboard.fingerprint && sourceSnapshot.scenes.length === payload.storyboard.scenes.length &&
          payload.storyboard.scenes.every(scene => {
            const source = sourceSnapshot.scenes.find(item => item.sceneOrder === scene.sceneOrder);
            return source && source.mockupId === scene.mockupId && source.sourceHash === scene.sourceHash &&
              source.imageStorageVersion && assets.has(`scene-sources/${scene.sceneOrder}.png`) &&
              (scene.generationStrategy !== "ai_shot" || source.clipFingerprint === payload.storyboard!.aiShots?.find(shot => shot.sceneOrder === scene.sceneOrder)?.fingerprint &&
                source.clipStorageVersion && assets.has(`scene-clips/${scene.sceneOrder}.mp4`));
          }));
        const sceneSource: NonNullable<ExportProduct["videos"][number]["snapshot"]> | null = scenesReady ? {
          videoId: payload.videoId, storyboardFingerprint: payload.storyboard.fingerprint,
          scenes: sourceSnapshot!.scenes.map(item => ({ sceneOrder: item.sceneOrder, mockupId: item.mockupId, sourceRevision: item.sourceRevision,
            sourceHash: item.sourceHash, imageVersion: item.imageStorageVersion!, imageFile: assets.get(`scene-sources/${item.sceneOrder}.png`)!.file,
            clipFingerprint: item.clipFingerprint, clipVersion: item.clipStorageVersion,
            clipFile: item.clipStorageKey ? assets.get(`scene-clips/${item.sceneOrder}.mp4`)?.file : null }))
        } : null;
        const mainVideo = assets.get("video.mp4"), thumbnail = assets.get("thumbnail.png");
        if (!mainVideo || !thumbnail) throw new Error("The approved video or thumbnail is missing");
        const packages = await packageBatchExport(directory, [{ id: payload.manifest.productId, name: payload.productName,
          type: payload.productType ?? "product", images: outputImages, listing: payload.listing ?? null,
          videos: [{ id: payload.videoId, version: payload.manifest.version, file: mainVideo.file,
            storageVersion: payload.videoStorageVersion ?? payload.manifest.fingerprint, thumbnailFile: thumbnail.file,
            thumbnailVersion: payload.thumbnailStorageVersion ?? payload.manifest.fingerprint,
            includeScenes: scenesReady, sceneWarning: scenesReady ? undefined : "Scene clips are unavailable for this video version; the final video is included.",
            storyboard: payload.storyboard, snapshot: sceneSource }] }], renderer, 400 * 1024 * 1024, signal);
        if (packages.length !== 1) throw new Error("Expected one ZIP package for this product");
        zip = packages[0]!;
      } else {
        for (const scene of payload.manifest.scenes) {
          const source = assets.get(scene.source);
          if (!source) throw new Error("Missing ZIP source");
          pngSource(source.data, scene.sourceHash);
        }
        // Model photo images: a mock-up put on a person, packed beside the video's sources.
        for (const photo of payload.manifest.modelPhotos ?? []) {
          const source = assets.get(photo.source);
          if (!source) throw new Error("Missing ZIP source");
          pngSource(source.data, photo.sourceHash);
        }
        await createZip(zip, assets, payload.manifest, signal);
      }
      stage = "Uploading"; progress = 90;
      const output = await uploadLocalPackage(job, zip, signal);
      await api(`${job.id}/complete`, { leaseToken: job.leaseToken, storageKey: output.key, storageVersion: output.sha256, outputBytes: output.bytes }, signal);
    }
    process.stdout.write(`Media job ${job.id} completed.\n`);
  } catch (error) {
    await api(`${job.id}/fail`, { leaseToken: job.leaseToken, message: failureCode(error, duringAiShot) }, shutdown.signal).catch(() => {});
    process.stderr.write(`Media job ${job.id} did not complete: ${error instanceof Error ? error.message : "unknown error"}.\n`);
  } finally {
    clearInterval(heartbeat); abort.abort();
    if (resolve(directory).startsWith(resolve(tmpdir()) + (process.platform === "win32" ? "\\" : "/")) && directory.includes("apcs-media-")) await rm(directory, { recursive: true, force: true });
  }
}
while (!shutdown.signal.aborted) {
  try {
    const result = await api("claim", {}, shutdown.signal);
    if (result) await execute(workerJobSchema.parse(result));
    else await delay(2500, undefined, { signal: shutdown.signal });
  } catch {
    if (shutdown.signal.aborted) break;
    process.stderr.write("Media queue unavailable; retrying.\n");
    await delay(5000, undefined, { signal: shutdown.signal }).catch(() => {});
  }
}
