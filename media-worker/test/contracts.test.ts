import assert from "node:assert/strict";
import { test } from "node:test";
import { exportPayloadSchema, storyboardSchema } from "../src/contracts";
import { pngSource } from "../src/media-files";
import { fixtureBoard, fixtureImage } from "./fixture";
test("storyboard pins exact frame total and Standard provenance", () => { const board = fixtureBoard(); assert.equal(storyboardSchema.parse(board).scenes.length, 2); assert.equal(storyboardSchema.safeParse({ ...board, durationSeconds: 4 }).success, false); assert.equal(storyboardSchema.safeParse({ ...board, scenes: [{ ...board.scenes[0], generationStrategy: "ai_shot" }, board.scenes[1]] }).success, false); });
test("AI Shot storyboard accepts a mixed 6-second clip and a full 15-second overlap", () => {
  const base = fixtureBoard("tshirt", fixtureImage(), 12);
  const ai = { sceneOrder: 0, mockupId: base.scenes[0]!.mockupId, sourceHash: base.scenes[0]!.sourceHash, model: "veo-3.1-lite-generate-preview", preset: "product_cinematic", prompt: "Smooth product motion", durationSeconds: 6, fingerprint: "b".repeat(64) };
  const mixed = { ...base, mode: "ai_shot", aiShots: [ai], scenes: [{ ...base.scenes[0], generationStrategy: "ai_shot", durationFrames: 180 }, { ...base.scenes[1], durationFrames: 180 }] };
  assert.equal(storyboardSchema.safeParse(mixed).success, true);
  const full = { ...base, mode: "ai_shot", durationSeconds: 15, transitionOverlapFrames: 30, aiShots: [{ ...ai, durationSeconds: 8 }, { ...ai, sceneOrder: 1, durationSeconds: 8 }], scenes: [{ ...base.scenes[0], generationStrategy: "ai_shot", durationFrames: 240 }, { ...base.scenes[1], generationStrategy: "ai_shot", durationFrames: 240 }] };
  assert.equal(storyboardSchema.safeParse(full).success, true);
  assert.equal(storyboardSchema.safeParse({ ...full, transitionOverlapFrames: 0 }).success, false);
});
test("transitioned storyboards use one overlap per scene boundary and validate output frames", () => {
  const base = fixtureBoard("tshirt", fixtureImage(), 12);
  const fourScenes = Array.from({ length: 4 }, (_, i) => ({ ...base.scenes[0]!, sceneOrder: i, durationFrames: 99, transition: i ? "whip_pan" as const : "none" as const }));
  const standard = { ...base, outputDurationFrames: 360, transitionOverlapFrames: 12, scenes: fourScenes };
  assert.equal(storyboardSchema.safeParse(standard).success, true);
  assert.equal(storyboardSchema.safeParse({ ...standard, outputDurationFrames: 372 }).success, false);
  assert.equal(storyboardSchema.safeParse({ ...standard, transitionOverlapFrames: 0 }).success, false);

  const legacy = { ...base, scenes: base.scenes.map(scene => ({ ...scene, transition: "cut" as const })) };
  assert.equal(storyboardSchema.safeParse(legacy).success, true);
});
test("immutable source verifies hash and PNG dimensions", () => { const image = fixtureImage(); const board = fixtureBoard("mug", image); const source = pngSource(image, board.scenes[0]!.sourceHash); assert.equal(source.width, 540); assert.equal(source.height, 1080); assert.throws(() => pngSource(image, "b".repeat(64)), /hash/); });
test("ZIP manifest rejects secrets or provider metadata", () => {
  const manifest = { schemaVersion: 1, productId: "fdc00975-356a-4340-857f-a1fa2c273241", videoId: "fdc00975-356a-4340-857f-a1fa2c273241", version: 1, mode: "standard", fingerprint: "a".repeat(64), approvedRevision: 2, video: "video.mp4", thumbnail: "thumbnail.png", scenes: [] };
  const payload = { exportId: manifest.videoId, videoId: manifest.videoId, manifest };
  assert.equal(exportPayloadSchema.safeParse(payload).success, true);
  assert.equal(exportPayloadSchema.safeParse({ ...payload, manifest: { ...manifest, prompt: "secret", signedUrl: "https://example.com" } }).success, false);
});
