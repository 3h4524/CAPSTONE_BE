import assert from "node:assert/strict";
import { test } from "node:test";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { packageBatchExport, type ExportProduct, type SceneRenderer } from "../src/batch-export";
import { fixtureBoard, fixtureImage } from "./fixture";

test("batch export renders scenes from the selected video snapshot and includes each approved image", async () => {
  const directory = await mkdtemp(join(tmpdir(), "apcs-batch-export-"));
  try {
    const id = "fdc00975-356a-4340-857f-a1fa2c273241";
    const image = fixtureImage(); const board = fixtureBoard("tshirt", image, 6);
    const input = join(directory, "input.png"); const videoFile = join(directory, "video.mp4");
    await writeFile(input, image); await writeFile(videoFile, "final");
    const snapshot = { videoId: id, storyboardFingerprint: board.fingerprint, scenes: board.scenes.map(scene => ({
      sceneOrder: scene.sceneOrder, mockupId: scene.mockupId, sourceRevision: scene.sourceRevision,
      sourceHash: scene.sourceHash, imageFile: input, imageVersion: "1",
    })) };
    const product: ExportProduct = { id, name: "Product", type: "tshirt", images: [
      { id: "mockup-1", file: input, path: "mockup/mockup-01.png" },
      { id: "mockup-2", file: input, path: "mockup/mockup-02.png" },
      { id: "design-1", file: input, path: "design/design-01.png" },
    ], listing: { title: "Áo", description: "Dòng 1\nDòng 2", tags: ["áo", "xanh"], version: 1 },
    videos: [{ id, version: 2, file: videoFile, storageVersion: "2", thumbnailFile: input, thumbnailVersion: "3", includeScenes: true, storyboard: board, snapshot }] };
    const rendered: number[] = [];
    const renderer: SceneRenderer = { async renderScene(props, target) {
      rendered.push(props.sceneOrder); assert.equal(props.finalVideo, videoFile);
      const file = join(target, `scene-${props.sceneOrder}.mp4`); await writeFile(file, `scene ${props.sceneOrder}`); return { video: file };
    } };
    const parts = await packageBatchExport(directory, [product], renderer, 4 * 1024 * 1024, AbortSignal.timeout(10_000));
    assert.deepEqual(rendered, [0, 1]);
    const zip = await readFile(parts[0]!);
    const root = `Product__${id}`; const version = `v002__${id}`;
    for (const name of [`${root}/mockup/mockup-01.png`, `${root}/mockup/mockup-02.png`, `${root}/design/design-01.png`,
      `${root}/listing/listing.json`, `${root}/video/final/${version}/video.mp4`, `${root}/video/final/${version}/thumbnail.png`,
      `${root}/video/scenes/${version}/scene-01.mp4`, `${root}/video/scenes/${version}/scene-02.mp4`])
      assert.ok(zip.includes(Buffer.from(name)), name);
    product.videos[0]!.snapshot!.videoId = "51200975-356a-4340-857f-a1fa2c273241";
    await assert.rejects(packageBatchExport(directory, [product], renderer, 4 * 1024 * 1024, AbortSignal.timeout(10_000)), /Scene sources unavailable/);
  } finally { await rm(directory, { recursive: true, force: true }); }
});

test("batch export rejects an AI clip whose fingerprint differs from the selected video", async () => {
  const directory = await mkdtemp(join(tmpdir(), "apcs-batch-ai-"));
  try {
    const id = "fdc00975-356a-4340-857f-a1fa2c273241";
    const image = fixtureImage(); const base = fixtureBoard("tshirt", image, 6);
    const input = join(directory, "image.png"); const videoFile = join(directory, "video.mp4");
    await writeFile(input, image); await writeFile(videoFile, "video");
    const scene = { ...base.scenes[0]!, generationStrategy: "ai_shot" as const };
    const board = { ...base, mode: "ai_shot" as const, scenes: [scene, base.scenes[1]!], aiShots: [{ sceneOrder: 0,
      mockupId: scene.mockupId, sourceHash: scene.sourceHash, model: "MiniMax-H3" as const,
      preset: "product_cinematic" as const, prompt: "Motion", durationSeconds: 6, fingerprint: "b".repeat(64) }] };
    const product: ExportProduct = { id, name: "Product", type: "tshirt", images: [], videos: [{ id, version: 1,
      file: videoFile, storageVersion: "1", thumbnailFile: input, thumbnailVersion: "1", includeScenes: true, storyboard: board,
      snapshot: { videoId: id, storyboardFingerprint: board.fingerprint, scenes: board.scenes.map(item => ({
        sceneOrder: item.sceneOrder, mockupId: item.mockupId, sourceRevision: item.sourceRevision, sourceHash: item.sourceHash,
        imageFile: input, imageVersion: "1", clipFile: videoFile, clipVersion: "1", clipFingerprint: "c".repeat(64),
      })) } }] };
    const renderer: SceneRenderer = { async renderScene() { throw new Error("Renderer must not run"); } };
    await assert.rejects(packageBatchExport(directory, [product], renderer, 4 * 1024 * 1024, AbortSignal.timeout(10_000)), /AI scene source unavailable/);
  } finally { await rm(directory, { recursive: true, force: true }); }
});
