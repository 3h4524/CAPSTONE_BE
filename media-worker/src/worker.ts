import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { exportPayloadSchema, renderPayloadSchema, workerJobSchema } from "./contracts";
import type { RenderProps, WorkerJob } from "./contracts";
import { createZip, downloadAssets, pngSource, uploadFile } from "./media-files";
import { RemotionVideoRenderer } from "./renderer";

const baseUrl = new URL(process.env.APCS_API_URL ?? "http://host.docker.internal:5191");
const key = process.env.MEDIA_WORKER_API_KEY ?? "";
if (key.length < 32 || key.length > 256) throw new Error("Set MEDIA_WORKER_API_KEY (32–256 characters)");
if (baseUrl.protocol !== "https:" && !["localhost", "127.0.0.1", "host.docker.internal", "api"].includes(baseUrl.hostname)) throw new Error("Worker requires HTTPS outside a local/container network");
const renderer = new RemotionVideoRenderer();
const shutdown = new AbortController();
process.once("SIGINT", () => shutdown.abort()); process.once("SIGTERM", () => shutdown.abort());
async function api(path: string, payload: unknown, signal: AbortSignal) {
  const response = await fetch(new URL(`/internal/media-jobs/${path}`, baseUrl), { method: "POST", headers: { "Content-Type": "application/json", "X-Media-Worker-Key": key }, body: JSON.stringify(payload), signal: AbortSignal.any([signal, AbortSignal.timeout(20_000)]) });
  if (!response.ok) throw new Error(`Internal API rejected request (${response.status})`);
  return response.status === 204 ? null : response.json();
}
async function execute(job: WorkerJob) {
  const directory = await mkdtemp(join(tmpdir(), "apcs-media-"));
  const abort = new AbortController();
  const signal = AbortSignal.any([abort.signal, shutdown.signal, AbortSignal.timeout(10 * 60_000)]);
  let stage = "Planning", progress = 5;
  let heartbeatRunning = false;
  const heartbeat = setInterval(() => {
    if (heartbeatRunning) return;
    heartbeatRunning = true;
    void api(`${job.id}/heartbeat`, { leaseToken: job.leaseToken, stage, progress }, signal).catch(() => abort.abort()).finally(() => { heartbeatRunning = false; });
  }, 15_000);
  try {
    stage = "Generating Assets"; progress = 10;
    const assets = await downloadAssets(job, directory, signal);
    if (job.kind === "render") {
      const payload = renderPayloadSchema.parse(job.payload);
      const sources: RenderProps["sources"] = {};
      for (const scene of payload.storyboard.scenes) {
        const asset = assets.get(`mockups/${scene.mockupId}.png`);
        if (!asset) throw new Error("Missing source mockup");
        sources[scene.mockupId] = pngSource(asset.data, scene.sourceHash);
      }
      stage = "Rendering";
      const result = await renderer.render({ storyboard: payload.storyboard, sources }, directory, signal, p => { progress = Math.max(progress, p); if (p >= 85) stage = "Technical QA"; });
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
  } catch {
    await api(`${job.id}/fail`, { leaseToken: job.leaseToken, message: "Media processing failed." }, shutdown.signal).catch(() => {});
    process.stderr.write(`Media job ${job.id} did not complete.\n`);
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
