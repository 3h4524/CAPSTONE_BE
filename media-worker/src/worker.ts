import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { exportPayloadSchema, renderPayloadSchema, workerJobSchema } from "./contracts";
import type { RenderProps, WorkerJob } from "./contracts";
import { ApiError, failureCode } from "./failures";
import { createZip, downloadAssets, pngSource, uploadFile } from "./media-files";
import { RemotionVideoRenderer } from "./renderer";

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
      for (const scene of payload.manifest.scenes) {
        const source = assets.get(scene.source);
        if (!source) throw new Error("Missing ZIP source");
        pngSource(source.data, scene.sourceHash);
      }
      stage = "Exporting"; progress = 40;
      const zip = join(directory, "package.zip");
      await createZip(zip, assets, payload.manifest, signal);
      if (!job.uploads.zip) throw new Error("Missing ZIP upload grant");
      stage = "Uploading"; progress = 90;
      const output = await uploadFile(job.uploads.zip, zip, signal);
      await api(`${job.id}/complete`, { leaseToken: job.leaseToken, ...output }, signal);
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
