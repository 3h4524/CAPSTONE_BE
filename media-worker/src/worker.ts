import { mkdtemp, rm, stat, writeFile } from "node:fs/promises";
import { createReadStream } from "node:fs";
import { cpus, tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { exportPayloadSchema, renderPayloadSchema, workerJobSchema } from "./contracts";
import type { RenderProps, WorkerJob } from "./contracts";
import { ApiError, failureCode } from "./failures";
import { createZip, downloadAssets, pngSource, uploadFile } from "./media-files";
import { readExportSceneConcurrency } from "./concurrency";
import { RemotionVideoRenderer, renderConcurrency } from "./renderer";
import { packageBatchExport, type ExportProduct } from "./batch-export";
import { runMediaStream } from "./media-stream";
import { followAiShot, readAiShotPollingSettings } from "./ai-shot-polling";

const baseUrl = new URL(process.env.APCS_API_URL ?? "http://host.docker.internal:5191");
const key = process.env.MEDIA_WORKER_API_KEY ?? "";
if (key.length < 32 || key.length > 256) throw new Error("Set MEDIA_WORKER_API_KEY (32–256 characters)");
if (baseUrl.protocol !== "https:" && !["localhost", "127.0.0.1", "host.docker.internal", "api"].includes(baseUrl.hostname)) throw new Error("Worker requires HTTPS outside a local/container network");
const renderer = new RemotionVideoRenderer();
// Bundle the composition now, so the first job does not wait for it.
void renderer.prewarm();
const shutdown = new AbortController();
process.once("SIGINT", () => shutdown.abort()); process.once("SIGTERM", () => shutdown.abort());
// Submit one paid AI video request at a time by default. Some accounts/models become unstable when
// multiple long-running generations are started together; operators can still opt into 2-4.
const configuredAiConcurrency = Number(process.env.AI_SCENE_CONCURRENCY ?? "1");
const aiSceneConcurrency = Number.isInteger(configuredAiConcurrency) && configuredAiConcurrency >= 1
  ? Math.min(configuredAiConcurrency, 4)
  : 1;
// Scenes of one export rendered at the same time (EXPORT_SCENE_CONCURRENCY): by default what fits in the host's cores, so a
// small host keeps rendering them one after another.
const exportSceneConcurrency = readExportSceneConcurrency(process.env.EXPORT_SCENE_CONCURRENCY, cpus().length, renderConcurrency);
// How often a scene still being generated is asked about: the provider takes minutes, so asking every few seconds
// only adds load on the API and the database.
const aiShotPolling = readAiShotPollingSettings(process.env);
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
  let settled = false;
  let heartbeatRunning = false;
  let consecutiveHeartbeatFailures = 0;
  // Seconds each stage took, written once when a render completes so a slow job shows where its time went.
  const timings: Record<string, number> = {};
  const seconds = (since: number) => Math.round((performance.now() - since) / 100) / 10;
  // From the claim to the API accepting the result: what the job cost beyond its own steps shows as the difference.
  const jobStarted = performance.now();
  /** Reports the result; `complete` is how long the API took to accept it (it also moves the run on, and may queue the next job). */
  const complete = async (body: unknown) => {
    const started = performance.now();
    await api(`${job.id}/complete`, body, signal);
    timings.complete = seconds(started);
    timings.total = seconds(jobStarted);
  };
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
    const downloadStarted = performance.now();
    const assets = await downloadAssets(job, directory, signal);
    timings.download = seconds(downloadStarted);
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
        state = await followAiShot(state, {
          start: async () => await api(`${job.id}/ai-shots/${shot.sceneOrder}/start`, request, signal) as { status: string },
          poll: async () => await api(`${job.id}/ai-shots/${shot.sceneOrder}/poll`, request, signal) as { status: string },
          delay: ms => delay(ms, undefined, { signal }),
          now: () => Date.now()
        }, aiShotPolling);
        if (state.status !== "ready") throw new Error(`AI Shot scene ${shot.sceneOrder + 1} did not finish`);
        const file = join(directory, `ai-shot-${shot.sceneOrder}.mp4`);
        await writeFile(file, await aiShotBytes(job, shot.sceneOrder, signal));
        const grant = job.uploads[`ai-shot-${shot.sceneOrder}`];
        if (!grant) throw new Error("Missing AI Shot upload grant");
        const result = await uploadFile(grant, file, signal);
        await api(`${job.id}/ai-shots/${shot.sceneOrder}/stored`, { ...request, ...result }, signal);
        clips[shot.sceneOrder] = file;
      };
      const shotsStarted = performance.now();
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
      if (aiShots.length > 0) timings.aiShots = seconds(shotsStarted);
      duringAiShot = false;
      stage = "Rendering";
      const result = await renderer.render({ storyboard: payload.storyboard, sources, clips, music }, directory, signal, p => { progress = Math.max(progress, p); if (p >= 85) stage = "Technical QA"; });
      stage = "Uploading"; progress = 90;
      if (!job.uploads.video || !job.uploads.thumbnail) throw new Error("Missing upload grants");
      const uploadStarted = performance.now();
      const [video, thumbnail] = await Promise.all([
        uploadFile(job.uploads.video, result.video, signal),
        uploadFile(job.uploads.thumbnail, result.thumbnail, signal)
      ]);
      timings.upload = seconds(uploadStarted);
      await complete({ leaseToken: job.leaseToken, ...video, thumbnailStorageKey: thumbnail.storageKey, thumbnailStorageVersion: thumbnail.storageVersion, qa: result.qa });
      Object.assign(timings, result.timings);
      process.stdout.write(`Media job ${job.id} timings (s): ${Object.entries(timings).map(([name, value]) => `${name}=${value}`).join(" ")}.\n`);
    } else {
      const payload = exportPayloadSchema.parse(job.payload);
      // Model photo images: a mock-up put on a person. The single-ZIP fallback below packs them beside the video's
      // sources; the product package already carries them among its approved mock-ups.
      for (const photo of payload.manifest.modelPhotos ?? []) {
        const source = assets.get(photo.source);
        if (!source) throw new Error("Missing ZIP source");
        pngSource(source.data, photo.sourceHash);
      }
      stage = "Exporting"; progress = 40;
      let zip = join(directory, "package.zip");
      const manifest = payload.manifest;
      // A product whose video was rejected at review is packaged the same way, with no video in it.
      if (payload.productName && (payload.storyboard || manifest.videoId === null)) {
        const outputImages: ExportProduct["images"] = [];
        for (const [index, mockup] of (payload.approvedMockups ?? []).entries()) {
          const source = assets.get(`mockups/${mockup.id}.png`);
          if (!source || !mockup.sha256) throw new Error("An approved mockup disappeared during ZIP export");
          pngSource(source.data, mockup.sha256);
          outputImages.push({ id: mockup.id, path: `mockup/mockup-${String(index + 1).padStart(2, "0")}.png`, file: source.file, version: mockup.storageVersion, sha256: mockup.sha256 });
        }
        for (const [index, design] of (payload.designs ?? []).entries()) {
          const source = assets.get(`designs/${design.id}`);
          if (!source) throw new Error("An approved design disappeared during ZIP export");
          const extension = (design.fileFormat ?? "png").toLowerCase();
          if (!/^[a-z0-9]{1,10}$/.test(extension)) throw new Error("An approved design has an invalid file format");
          outputImages.push({ id: design.id, path: `design/design-${String(index + 1).padStart(2, "0")}.${extension}`, file: source.file });
        }
        const videos: ExportProduct["videos"] = [];
        const storyboard = payload.storyboard;
        if (manifest.videoId !== null && storyboard) {
          const videoId = manifest.videoId;
          const sourceSnapshot = payload.sources;
          const scenesReady = Boolean(sourceSnapshot && sourceSnapshot.videoId === videoId &&
            sourceSnapshot.storyboardFingerprint === storyboard.fingerprint && sourceSnapshot.scenes.length === storyboard.scenes.length &&
            storyboard.scenes.every(scene => {
              const source = sourceSnapshot.scenes.find(item => item.sceneOrder === scene.sceneOrder);
              return source && source.mockupId === scene.mockupId && source.sourceHash === scene.sourceHash &&
                source.imageStorageVersion && assets.has(`scene-sources/${scene.sceneOrder}.png`) &&
                (scene.generationStrategy !== "ai_shot" || source.clipFingerprint === storyboard.aiShots?.find(shot => shot.sceneOrder === scene.sceneOrder)?.fingerprint &&
                  source.clipStorageVersion && assets.has(`scene-clips/${scene.sceneOrder}.mp4`));
            }));
          const sceneSource: NonNullable<ExportProduct["videos"][number]["snapshot"]> | null = scenesReady ? {
            videoId, storyboardFingerprint: storyboard.fingerprint,
            scenes: sourceSnapshot!.scenes.map(item => ({ sceneOrder: item.sceneOrder, mockupId: item.mockupId, sourceRevision: item.sourceRevision,
              sourceHash: item.sourceHash, imageVersion: item.imageStorageVersion!, imageFile: assets.get(`scene-sources/${item.sceneOrder}.png`)!.file,
              clipFingerprint: item.clipFingerprint, clipVersion: item.clipStorageVersion,
              clipFile: item.clipStorageKey ? assets.get(`scene-clips/${item.sceneOrder}.mp4`)?.file : null }))
          } : null;
          // The package has the final video only when the API asked for it; otherwise it carries the video of every scene alone.
          const wantsFinal = Boolean(payload.videoStorageKey);
          const mainVideo = assets.get("video.mp4"), thumbnail = assets.get("thumbnail.png");
          if (wantsFinal && (!mainVideo || !thumbnail)) throw new Error("The approved video or thumbnail is missing");
          if (!wantsFinal && !scenesReady) throw new Error("Scene sources are unavailable, so there is no video to package");
          videos.push({ id: videoId, version: manifest.version, storageVersion: payload.videoStorageVersion ?? manifest.fingerprint,
            file: wantsFinal ? mainVideo!.file : undefined, thumbnailFile: wantsFinal ? thumbnail!.file : undefined,
            thumbnailVersion: wantsFinal ? payload.thumbnailStorageVersion ?? manifest.fingerprint : undefined,
            includeScenes: scenesReady, sceneWarning: scenesReady ? undefined : "Scene clips are unavailable for this video version; the final video is included.",
            storyboard, snapshot: sceneSource });
        }
        const packages = await packageBatchExport(directory, [{ id: manifest.productId, name: payload.productName,
          type: payload.productType ?? "product", images: outputImages, listing: payload.listing ?? null, guide: payload.etsyGuide ?? null,
          videos }], renderer, 400 * 1024 * 1024, signal, { sceneConcurrency: exportSceneConcurrency, timings });
        if (packages.length !== 1) throw new Error("Expected one ZIP package for this product");
        zip = packages[0]!;
      } else {
        if (manifest.videoId === null) throw new Error("A package without a video needs its product");
        for (const scene of manifest.scenes) {
          const source = assets.get(scene.source);
          if (!source) throw new Error("Missing ZIP source");
          pngSource(source.data, scene.sourceHash);
        }
        const zipStarted = performance.now();
        await createZip(zip, assets, manifest, signal);
        timings.zip = seconds(zipStarted);
      }
      stage = "Uploading"; progress = 90;
      const uploadStarted = performance.now();
      const output = await uploadLocalPackage(job, zip, signal);
      timings.upload = seconds(uploadStarted);
      await complete({ leaseToken: job.leaseToken, storageKey: output.key, storageVersion: output.sha256, outputBytes: output.bytes });
      // Where an export's time went, as a render's is written: its video's render is not here, it is a job of its own.
      const megabytes = Math.round(output.bytes / 104_857.6) / 10;
      process.stdout.write(`Media job ${job.id} export timings (s): ${Object.entries(timings).map(([name, value]) => `${name}=${value}`).join(" ")} sceneConcurrency=${exportSceneConcurrency} zipMB=${megabytes}.\n`);
    }
    settled = true;
    process.stdout.write(`Media job ${job.id} completed.\n`);
  } catch (error) {
    try { await api(`${job.id}/fail`, { leaseToken: job.leaseToken, message: failureCode(error, duringAiShot) }, shutdown.signal); settled = true; }
    catch { /* Keep stream message pending until the lease is reconciled. */ }
    process.stderr.write(`Media job ${job.id} did not complete: ${error instanceof Error ? error.message : "unknown error"}.\n`);
  } finally {
    clearInterval(heartbeat); abort.abort();
    if (resolve(directory).startsWith(resolve(tmpdir()) + (process.platform === "win32" ? "\\" : "/")) && directory.includes("apcs-media-")) await rm(directory, { recursive: true, force: true });
  }
  return settled;
}
const queueMode = (process.env.MEDIA_QUEUE_MODE ?? "polling").toLowerCase();
if (queueMode !== "polling" && queueMode !== "streams") throw new Error("MEDIA_QUEUE_MODE must be polling or streams");
if (queueMode === "streams") {
  await runMediaStream(shutdown.signal, async event => {
    while (!shutdown.signal.aborted) {
      const result = await api(`${event.jobId}/claim`, { queueVersion: event.queueVersion }, shutdown.signal) as { status?: string; job?: unknown };
      if (result.status === "obsolete") return true;
      if (result.status === "busy") return false;
      if (result.status === "claimed") return execute(workerJobSchema.parse(result.job));
      if (result.status !== "not_due") throw new Error("Unexpected media claim response");
      await delay(5000, undefined, { signal: shutdown.signal });
    }
    return false;
  });
} else while (!shutdown.signal.aborted) {
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
