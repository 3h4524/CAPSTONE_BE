import { mkdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { createBatchZipParts, type BatchZipAsset, type BatchZipListing, type BatchZipProduct } from "./batch-zip";
import type { SceneRenderProps, Storyboard } from "./contracts";
import { mapWithConcurrency } from "./concurrency";
import { pngSource } from "./media-files";

/** Resolved from a server-authorized snapshot, never from paths or URLs supplied by the client. */
export type ExportSceneSource = {
  sceneOrder: number; mockupId: string; sourceRevision: number; sourceHash: string;
  imageFile: string; imageVersion: string;
  clipFile?: string | null; clipVersion?: string | null; clipFingerprint?: string | null;
};
/** `file`, `thumbnailFile` and `thumbnailVersion` are there only when the package carries the final video. */
export type ExportVideo = {
  id: string; version: number; storageVersion: string; file?: string; thumbnailFile?: string; thumbnailVersion?: string;
  includeScenes: boolean; sceneWarning?: string; storyboard?: Storyboard | null;
  snapshot?: { videoId: string; storyboardFingerprint: string; scenes: ExportSceneSource[] } | null;
};
export type ExportProduct = {
  id: string; name: string; type: string; images: BatchZipAsset[];
  videos: ExportVideo[]; listing?: BatchZipListing | null; guide?: string | null;
};
export interface SceneRenderer {
  /**
   * `timings` is the seconds each step of the scene took, when the renderer measures them; `cutFrom` says what the scene was cut
   * from with FFmpeg instead of being drawn in the browser: its AI clip, or the final video.
   */
  renderScene(props: SceneRenderProps, directory: string, signal: AbortSignal): Promise<{ video: string; timings?: Record<string, number>; cutFrom?: "clip" | "final" | null }>;
}
export type PackageOptions = {
  /** Scenes of one video rendered at the same time; 1 (the default) renders them one after another. */
  sceneConcurrency?: number;
  /**
   * Filled with where the time went, in seconds: `scenes` and `zip` are the time that passed; `scene<Step>` adds up what a
   * step took over every scene, which is more than the time that passed when scenes run side by side; `scene<NN><Kind>` is what
   * each scene took on its own, Kind being Clip (an AI scene cut from its clip), Final (cut from the final video), Ai (an AI
   * scene drawn) or Photo (a photo motion scene drawn).
   */
  timings?: Record<string, number>;
};

/** Build product entries and render edited scenes from the selected video's immutable snapshot. */
export async function packageBatchExport(directory: string, products: ExportProduct[], renderer: SceneRenderer,
  maxPartBytes: number, signal: AbortSignal, options: PackageOptions = {}) {
  const timings = options.timings;
  const add = (name: string, seconds: number) => { if (timings) timings[name] = Math.round(((timings[name] ?? 0) + seconds) * 10) / 10; };
  const output: BatchZipProduct[] = [];
  for (const [productIndex, product] of products.entries()) {
    const assets = [...product.images];
    for (const [videoIndex, video] of product.videos.entries()) {
      signal.throwIfAborted();
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(video.id) ||
          !Number.isSafeInteger(video.version) || video.version < 1 || !video.storageVersion || (video.file && (!video.thumbnailFile || !video.thumbnailVersion)))
        throw new Error("Invalid selected video version");
      const version = `v${String(video.version).padStart(3, "0")}__${video.id}`;
      if (video.file) {
        assets.push({ path: `video/final/${version}/video.mp4`, file: video.file, id: video.id, version: video.storageVersion });
        assets.push({ path: `video/final/${version}/thumbnail.png`, file: video.thumbnailFile!, id: video.id, version: video.thumbnailVersion! });
      }
      if (!video.includeScenes) continue;
      const board = video.storyboard, snapshot = video.snapshot;
      if (!board || !snapshot || snapshot.videoId !== video.id || snapshot.storyboardFingerprint !== board.fingerprint ||
          snapshot.scenes.length !== board.scenes.length)
        throw new Error(`Scene sources unavailable for video ${video.id}`);
      const sources: SceneRenderProps["sources"] = {};
      const clips: NonNullable<SceneRenderProps["clips"]> = {};
      for (const scene of board.scenes) {
        const matches = snapshot.scenes.filter(item => item.sceneOrder === scene.sceneOrder);
        const source = matches[0];
        if (matches.length !== 1 || !source || source.mockupId !== scene.mockupId || source.sourceRevision !== scene.sourceRevision ||
            source.sourceHash !== scene.sourceHash || !source.imageVersion)
          throw new Error(`Scene sources unavailable for video ${video.id}`);
        sources[scene.mockupId] = pngSource(await readFile(source.imageFile), source.sourceHash);
        if (scene.generationStrategy === "ai_shot") {
          const shot = board.aiShots?.find(item => item.sceneOrder === scene.sceneOrder);
          if (!shot || shot.mockupId !== scene.mockupId || shot.sourceHash !== scene.sourceHash ||
              shot.fingerprint !== source.clipFingerprint || !source.clipFile || !source.clipVersion)
            throw new Error(`AI scene source unavailable for video ${video.id}`);
          clips[scene.sceneOrder] = source.clipFile;
        }
      }
      const sceneDirectory = join(directory, `scenes-${productIndex}-${videoIndex}`);
      await mkdir(sceneDirectory, { recursive: true });
      // The scenes do not depend on each other, and each writes files named for itself: they can be rendered side by side.
      // Their files are added in scene order whichever finishes first, so the package is the same either way.
      const scenesStarted = performance.now();
      const rendered = await mapWithConcurrency(board.scenes, options.sceneConcurrency ?? 1, async scene => {
        signal.throwIfAborted();
        const started = performance.now();
        const result = await renderer.renderScene({ storyboard: board, sceneOrder: scene.sceneOrder, sources, clips,
          ...(video.file ? { finalVideo: video.file } : {}) }, sceneDirectory, signal);
        for (const [step, seconds] of Object.entries(result.timings ?? {})) add(`scene${step[0]!.toUpperCase()}${step.slice(1)}`, seconds);
        const kind = result.cutFrom === "clip" ? "Clip" : result.cutFrom === "final" ? "Final" : scene.generationStrategy === "ai_shot" ? "Ai" : "Photo";
        add(`scene${String(scene.sceneOrder + 1).padStart(2, "0")}${kind}`, (performance.now() - started) / 1000);
        return result;
      });
      add("scenes", (performance.now() - scenesStarted) / 1000);
      for (const [index, scene] of board.scenes.entries())
        assets.push({ path: `video/scenes/${version}/scene-${String(scene.sceneOrder + 1).padStart(2, "0")}.mp4`,
          file: rendered[index]!.video, id: video.id, version: video.storageVersion });
    }
    output.push({ id: product.id, name: product.name, type: product.type, assets, listing: product.listing, guide: product.guide,
      warnings: product.videos.filter(video => !video.includeScenes && video.sceneWarning).map(video => video.sceneWarning!) });
  }
  const zipStarted = performance.now();
  try { return await createBatchZipParts(directory, output, maxPartBytes, signal); }
  finally { add("zip", (performance.now() - zipStarted) / 1000); }
}
