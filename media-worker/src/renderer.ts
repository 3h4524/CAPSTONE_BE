import { bundle } from "@remotion/bundler";
import { makeCancelSignal, renderMedia, renderStill, selectComposition } from "@remotion/renderer";
import { fileURLToPath } from "node:url";
import { join } from "node:path";
import { createServer } from "node:http";
import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import { processOutput } from "./process";
import { inspectVideo } from "./qa";
import type { MediaQa, RenderProps } from "./contracts";

export interface IVideoRenderer { render(props: RenderProps, directory: string, signal: AbortSignal, progress: (percent: number) => void): Promise<{ video: string; thumbnail: string; qa: MediaQa }> }
export class RemotionVideoRenderer implements IVideoRenderer {
  private bundlePromise: Promise<string> | undefined;
  async render(props: RenderProps, directory: string, signal: AbortSignal, progress: (percent: number) => void) {
    this.bundlePromise ??= bundle({ entryPoint: fileURLToPath(new URL("./composition.tsx", import.meta.url)) });
    const serveUrl = await this.bundlePromise;
    const clipServer = await serveLocalClips(props.clips ?? {});
    const inputProps = { ...props, clips: clipServer.urls };
    const browserExecutable = process.env.CHROME_EXECUTABLE;
    const { cancel, cancelSignal } = makeCancelSignal();
    signal.addEventListener("abort", cancel, { once: true });
    const raw = join(directory, "render.mp4");
    const video = join(directory, "video.mp4");
    const thumbnail = join(directory, "thumbnail.png");
    try {
      const composition = await selectComposition({ serveUrl, id: "APCS-Standard", inputProps, browserExecutable });
      signal.throwIfAborted();
      await renderMedia({ composition, serveUrl, inputProps, outputLocation: raw, codec: "h264", pixelFormat: "yuv420p", crf: 20, muted: true, concurrency: 2, browserExecutable, cancelSignal, onProgress: p => progress(Math.round(p.progress * 70) + 10) });
      await processOutput(process.env.FFMPEG_PATH ?? "ffmpeg", ["-y", "-i", raw, "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-r", "30", "-movflags", "+faststart", "-map_metadata", "-1", video], signal);
      await renderStill({ composition, serveUrl, inputProps, output: thumbnail, imageFormat: "png", frame: 10, browserExecutable });
      progress(85);
      return { video, thumbnail, qa: await inspectVideo(video, props.storyboard.durationSeconds, signal, props.storyboard.width, props.storyboard.height) };
    } finally { signal.removeEventListener("abort", cancel); await clipServer.close(); }
  }
}

async function serveLocalClips(clips: Record<number, string>) {
  const token = randomUUID();
  const files = new Map(Object.entries(clips).map(([order, path]) => [`/${token}/${order}`, path]));
  if (files.size === 0) return { urls: {} as Record<number, string>, close: async () => {} };
  const server = createServer(async (request, response) => {
    const file = files.get(request.url?.split("?")[0] ?? "");
    if (!file || !["GET", "HEAD"].includes(request.method ?? "")) { response.writeHead(404).end(); return; }
    try {
      const info = await stat(file);
      const match = /^bytes=(\d+)-(\d*)$/.exec(request.headers.range ?? "");
      const start = match ? Number(match[1]) : 0;
      const end = match && match[2] ? Math.min(Number(match[2]), info.size - 1) : info.size - 1;
      if (start < 0 || start > end || end >= info.size) { response.writeHead(416).end(); return; }
      response.writeHead(match ? 206 : 200, { "Content-Type": "video/mp4", "Accept-Ranges": "bytes", "Content-Length": end - start + 1,
        ...(match ? { "Content-Range": `bytes ${start}-${end}/${info.size}` } : {}) });
      if (request.method === "HEAD") { response.end(); return; }
      createReadStream(file, { start, end }).pipe(response);
    } catch { response.writeHead(404).end(); }
  });
  await new Promise<void>((resolve, reject) => server.listen(0, "127.0.0.1", () => resolve()).on("error", reject));
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("Could not start local clip server");
  return { urls: Object.fromEntries([...files.keys()].map(route => [Number(route.split("/").at(-1)), `http://127.0.0.1:${address.port}${route}`])) as Record<number, string>,
    close: () => new Promise<void>((resolve, reject) => { server.closeAllConnections(); server.close(error => error ? reject(error) : resolve()); }) };
}
