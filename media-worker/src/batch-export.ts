import { mkdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { createBatchZipParts, type BatchZipAsset, type BatchZipListing, type BatchZipProduct } from "./batch-zip";
import type { SceneRenderProps, Storyboard } from "./contracts";
import { pngSource } from "./media-files";

/** Resolved from a server-authorized snapshot, never from paths or URLs supplied by the client. */
export type ExportSceneSource = {
  sceneOrder: number; mockupId: string; sourceRevision: number; sourceHash: string;
  imageFile: string; imageVersion: string;
  clipFile?: string | null; clipVersion?: string | null; clipFingerprint?: string | null;
};
export type ExportVideo = {
  id: string; version: number; file: string; storageVersion: string; thumbnailFile: string; thumbnailVersion: string;
  includeScenes: boolean; sceneWarning?: string; storyboard?: Storyboard | null;
  snapshot?: { videoId: string; storyboardFingerprint: string; scenes: ExportSceneSource[] } | null;
};
export type ExportProduct = {
  id: string; name: string; type: string; images: BatchZipAsset[];
  videos: ExportVideo[]; listing?: BatchZipListing | null; guide?: string | null;
};
export interface SceneRenderer {
  renderScene(props: SceneRenderProps, directory: string, signal: AbortSignal): Promise<{ video: string }>;
}

/** Build product entries and render edited scenes from the selected video's immutable snapshot. */
export async function packageBatchExport(directory: string, products: ExportProduct[], renderer: SceneRenderer,
  maxPartBytes: number, signal: AbortSignal) {
  const output: BatchZipProduct[] = [];
  for (const [productIndex, product] of products.entries()) {
    const assets = [...product.images];
    for (const [videoIndex, video] of product.videos.entries()) {
      signal.throwIfAborted();
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(video.id) ||
          !Number.isSafeInteger(video.version) || video.version < 1 || !video.storageVersion || !video.thumbnailVersion)
        throw new Error("Invalid selected video version");
      const version = `v${String(video.version).padStart(3, "0")}__${video.id}`;
      assets.push({ path: `video/final/${version}/video.mp4`, file: video.file, id: video.id, version: video.storageVersion });
      assets.push({ path: `video/final/${version}/thumbnail.png`, file: video.thumbnailFile, id: video.id, version: video.thumbnailVersion });
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
      for (const scene of board.scenes) {
        const rendered = await renderer.renderScene({ storyboard: board, sceneOrder: scene.sceneOrder, sources, clips, finalVideo: video.file }, sceneDirectory, signal);
        assets.push({ path: `video/scenes/${version}/scene-${String(scene.sceneOrder + 1).padStart(2, "0")}.mp4`,
          file: rendered.video, id: video.id, version: video.storageVersion });
      }
    }
    output.push({ id: product.id, name: product.name, type: product.type, assets, listing: product.listing, guide: product.guide,
      warnings: product.videos.filter(video => !video.includeScenes && video.sceneWarning).map(video => video.sceneWarning!) });
  }
  return createBatchZipParts(directory, output, maxPartBytes, signal);
}
