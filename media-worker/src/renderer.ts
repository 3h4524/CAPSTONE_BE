import { bundle } from "@remotion/bundler";
import { makeCancelSignal, renderMedia, renderStill, selectComposition } from "@remotion/renderer";
import { fileURLToPath } from "node:url";
import { join } from "node:path";
import { processOutput } from "./process";
import { inspectVideo } from "./qa";
import type { MediaQa, RenderProps } from "./contracts";

export interface IVideoRenderer { render(props: RenderProps, directory: string, signal: AbortSignal, progress: (percent: number) => void): Promise<{ video: string; thumbnail: string; qa: MediaQa }> }
export class RemotionVideoRenderer implements IVideoRenderer {
  private bundlePromise: Promise<string> | undefined;
  async render(props: RenderProps, directory: string, signal: AbortSignal, progress: (percent: number) => void) {
    this.bundlePromise ??= bundle({ entryPoint: fileURLToPath(new URL("./composition.tsx", import.meta.url)) });
    const serveUrl = await this.bundlePromise;
    const browserExecutable = process.env.CHROME_EXECUTABLE;
    const composition = await selectComposition({ serveUrl, id: "APCS-Standard", inputProps: props, browserExecutable });
    const { cancel, cancelSignal } = makeCancelSignal();
    signal.addEventListener("abort", cancel, { once: true });
    const raw = join(directory, "render.mp4");
    const video = join(directory, "video.mp4");
    const thumbnail = join(directory, "thumbnail.png");
    try {
      signal.throwIfAborted();
      await renderMedia({ composition, serveUrl, inputProps: props, outputLocation: raw, codec: "h264", pixelFormat: "yuv420p", crf: 20, muted: true, concurrency: 2, browserExecutable, cancelSignal, onProgress: p => progress(Math.round(p.progress * 70) + 10) });
      await processOutput(process.env.FFMPEG_PATH ?? "ffmpeg", ["-y", "-i", raw, "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-r", "30", "-movflags", "+faststart", "-map_metadata", "-1", video], signal);
      await renderStill({ composition, serveUrl, inputProps: props, output: thumbnail, imageFormat: "png", frame: 10, browserExecutable });
      progress(85);
      return { video, thumbnail, qa: await inspectVideo(video, props.storyboard.durationSeconds, signal, props.storyboard.width, props.storyboard.height) };
    } finally { signal.removeEventListener("abort", cancel); }
  }
}
