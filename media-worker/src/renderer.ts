import { bundle } from "@remotion/bundler";
import { makeCancelSignal, openBrowser, renderMedia, selectComposition } from "@remotion/renderer";
import { fileURLToPath } from "node:url";
import { join } from "node:path";
import { createServer } from "node:http";
import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import { cpus } from "node:os";
import { processOutput } from "./process";
import { inspectVideo } from "./qa";
import { directSceneArguments, finalVideoSceneArguments } from "./direct-scene";
import type { MediaQa, RenderProps, SceneRenderProps } from "./contracts";

// Remotion refuses a concurrency above the machine's core count, so a one-core host cannot render with 2.
export const renderConcurrency = Math.max(1, Math.min(2, cpus().length));

const x264Presets = ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow", "placebo"] as const;
type X264Preset = typeof x264Presets[number];
/** RENDER_X264_PRESET trades encode time for file size. Unset keeps Remotion's own default, so output does not change. */
function readX264Preset(value: string | undefined): X264Preset | undefined {
  const preset = value?.trim().toLowerCase();
  return x264Presets.find(item => item === preset);
}
const x264Preset = readX264Preset(process.env.RENDER_X264_PRESET);

// Without CHROME_EXECUTABLE Remotion uses its own Chrome Headless Shell, which draws frames much faster than a full Chrome
// or the distribution's Chromium. Set the variable only to force a particular browser.
const browserExecutable = () => process.env.CHROME_EXECUTABLE || null;
const ffmpeg = () => process.env.FFMPEG_PATH ?? "ffmpeg";

/**
 * Remotion already wrote h264/yuv420p/30 fps (and AAC when there is music): only the container needs fixing for streaming.
 * That holds because renderMedia is given colorSpace "bt709". Left at its default, Remotion writes full-range yuvj420p, which
 * only the second encode used to turn into the limited-range yuv420p the technical QA requires.
 */
export const remuxArguments = (input: string, output: string) =>
  ["-y", "-i", input, "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", "-map_metadata", "-1", output];
/** One PNG of frame 10, the frame the thumbnail has always been taken from. */
export const thumbnailArguments = (video: string, output: string) =>
  ["-y", "-i", video, "-vf", "select=eq(n\\,10)", "-frames:v", "1", output];

/** Runs a step and records how long it took, in seconds. */
async function timed<T>(timings: Record<string, number>, name: string, step: () => Promise<T>): Promise<T> {
  const started = performance.now();
  try { return await step(); }
  finally { timings[name] = Math.round((performance.now() - started) / 100) / 10; }
}

export interface RenderTimings { [step: string]: number }
export interface IVideoRenderer { render(props: RenderProps, directory: string, signal: AbortSignal, progress: (percent: number) => void): Promise<{ video: string; thumbnail: string; qa: MediaQa; timings: RenderTimings }> }
export class RemotionVideoRenderer implements IVideoRenderer {
  private bundlePromise: Promise<string> | undefined;
  private bundled() {
    // A failed bundle is forgotten, so the next job tries again instead of failing for the life of the process.
    const promise = this.bundlePromise ??= bundle({ entryPoint: fileURLToPath(new URL("./composition.tsx", import.meta.url)) });
    promise.catch(() => { if (this.bundlePromise === promise) this.bundlePromise = undefined; });
    return promise;
  }
  /** Bundles the composition ahead of the first job, which otherwise waits for it. */
  async prewarm() {
    try { await this.bundled(); }
    catch (error) { process.stderr.write(`Could not prepare the render bundle yet: ${error instanceof Error ? error.message : "unknown error"}.\n`); }
  }
  async renderScene(props: SceneRenderProps, directory: string, signal: AbortSignal) {
    const scene = props.storyboard.scenes.find(item => item.sceneOrder === props.sceneOrder);
    if (!scene) throw new Error("Scene is not in the video snapshot");
    // Seconds each step of this scene took, so an export that is slow shows which step it was.
    const timings: RenderTimings = {};
    const output = join(directory, `scene-${scene.sceneOrder + 1}.mp4`);
    const quality = () => inspectVideo(output, scene.durationFrames / 30, signal, props.storyboard.width, props.storyboard.height, false);
    // FFmpeg makes a scene in a second or two where it can, without drawing each frame in a browser: an AI scene with no text
    // from its own clip (the first encode of those pixels), else a scene with no transition into it from the final video. Should
    // that fail, the scene is drawn as any other.
    const cuts = [
      { from: "clip" as const, args: directSceneArguments(props, props.clips?.[scene.sceneOrder], output, x264Preset) },
      { from: "final" as const, args: finalVideoSceneArguments(props, output, x264Preset) },
    ];
    for (const { from, args } of cuts) {
      if (!args) continue;
      try {
        await timed(timings, "cut", () => processOutput(ffmpeg(), args, signal));
        const qa = await timed(timings, "qa", quality);
        return { video: output, qa, timings, cutFrom: from };
      } catch (error) {
        signal.throwIfAborted();
        delete timings.cut; delete timings.qa;
        process.stderr.write(`Scene ${scene.sceneOrder + 1} could not be cut from its ${from === "clip" ? "clip" : "final video"} (${error instanceof Error ? error.message : "unknown error"}).\n`);
      }
    }
    const serveUrl = await timed(timings, "bundle", () => this.bundled());
    const clipServer = await serveLocalClips({ ...props.clips });
    const { finalVideo: _cutOnly, ...drawn } = props;
    const inputProps = { ...drawn, clips: clipServer.urls };
    const { cancel, cancelSignal } = makeCancelSignal();
    signal.addEventListener("abort", cancel, { once: true });
    const raw = join(directory, `scene-${scene.sceneOrder + 1}-raw.mp4`);
    const browser = await timed(timings, "browser", () => openBrowser("chrome", { browserExecutable: browserExecutable() }));
    try {
      const composition = await timed(timings, "select", () => selectComposition({ serveUrl, id: "APCS-Export-Scene", inputProps, puppeteerInstance: browser }));
      await timed(timings, "frames", () => renderMedia({ composition, serveUrl, inputProps, outputLocation: raw, codec: "h264", pixelFormat: "yuv420p", crf: 20, x264Preset, colorSpace: "bt709",
        muted: true, concurrency: renderConcurrency, puppeteerInstance: browser, cancelSignal }));
      await timed(timings, "mux", () => processOutput(ffmpeg(), remuxArguments(raw, output), signal));
      const qa = await timed(timings, "qa", quality);
      return { video: output, qa, timings, cutFrom: null };
    } finally {
      signal.removeEventListener("abort", cancel);
      await browser.close({ silent: true }).catch(() => {});
      await clipServer.close();
    }
  }
  async render(props: RenderProps, directory: string, signal: AbortSignal, progress: (percent: number) => void) {
    const timings: RenderTimings = {};
    const serveUrl = await timed(timings, "bundle", () => this.bundled());
    const clipServer = await serveLocalClips(props.clips ?? {}, props.music);
    const inputProps = { ...props, clips: clipServer.urls, music: clipServer.music };
    const { cancel, cancelSignal } = makeCancelSignal();
    signal.addEventListener("abort", cancel, { once: true });
    const raw = join(directory, "render.mp4");
    const video = join(directory, "video.mp4");
    const thumbnail = join(directory, "thumbnail.png");
    // One browser for the whole render: opening it for each step costs a few seconds every time.
    const browser = await openBrowser("chrome", { browserExecutable: browserExecutable() });
    try {
      const composition = await selectComposition({ serveUrl, id: "APCS-Standard", inputProps, puppeteerInstance: browser });
      signal.throwIfAborted();
      const includesMusic = Boolean(props.storyboard.videoEdit?.music && !props.storyboard.videoEdit.music.muted);
      await timed(timings, "frames", () => renderMedia({ composition, serveUrl, inputProps, outputLocation: raw, codec: "h264", pixelFormat: "yuv420p", crf: 20, x264Preset, colorSpace: "bt709",
        muted: !includesMusic, concurrency: renderConcurrency, puppeteerInstance: browser, cancelSignal, onProgress: p => progress(Math.round(p.progress * 70) + 10) }));
      await timed(timings, "mux", () => processOutput(ffmpeg(), remuxArguments(raw, video), signal));
      await timed(timings, "thumbnail", () => processOutput(ffmpeg(), thumbnailArguments(video, thumbnail), signal));
      progress(85);
      const durationSeconds = (props.storyboard.outputDurationFrames ?? props.storyboard.durationSeconds * 30) / 30;
      const qa = await timed(timings, "qa", () => inspectVideo(video, durationSeconds, signal, props.storyboard.width, props.storyboard.height, includesMusic));
      return { video, thumbnail, qa, timings };
    } finally {
      signal.removeEventListener("abort", cancel);
      await browser.close({ silent: true }).catch(() => {});
      await clipServer.close();
    }
  }
}

async function serveLocalClips(clips: Record<number, string>, music?: string) {
  const token = randomUUID();
  const files = new Map<string, string>(Object.entries(clips).map(([order, path]) => [`/${token}/${order}`, path]));
  if (music) files.set(`/${token}/music`, music);
  if (files.size === 0) return { urls: {} as Record<number, string>, music: undefined as string | undefined, close: async () => {} };
  const server = createServer(async (request, response) => {
    const file = files.get(request.url?.split("?")[0] ?? "");
    if (!file || !["GET", "HEAD"].includes(request.method ?? "")) { response.writeHead(404).end(); return; }
    try {
      const info = await stat(file);
      const match = /^bytes=(\d+)-(\d*)$/.exec(request.headers.range ?? "");
      const start = match ? Number(match[1]) : 0;
      const end = match && match[2] ? Math.min(Number(match[2]), info.size - 1) : info.size - 1;
      if (start < 0 || start > end || end >= info.size) { response.writeHead(416).end(); return; }
      response.writeHead(match ? 206 : 200, { "Content-Type": file.endsWith(".mp3") ? "audio/mpeg" : file.endsWith(".wav") ? "audio/wav" : "video/mp4", "Accept-Ranges": "bytes", "Content-Length": end - start + 1,
        ...(match ? { "Content-Range": `bytes ${start}-${end}/${info.size}` } : {}) });
      if (request.method === "HEAD") { response.end(); return; }
      createReadStream(file, { start, end }).pipe(response);
    } catch { response.writeHead(404).end(); }
  });
  await new Promise<void>((resolve, reject) => server.listen(0, "127.0.0.1", () => resolve()).on("error", reject));
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("Could not start local clip server");
  return { urls: Object.fromEntries([...Object.keys(clips)].map(order => [Number(order), `http://127.0.0.1:${address.port}/${token}/${order}`])) as Record<number, string>, music: music ? `http://127.0.0.1:${address.port}/${token}/music` : undefined,
    close: () => new Promise<void>((resolve, reject) => { server.closeAllConnections(); server.close(error => error ? reject(error) : resolve()); }) };
}
