import assert from "node:assert/strict";
import { test } from "node:test";
import { exportPayloadSchema, storyboardSchema } from "../src/contracts";
import { pngSource } from "../src/media-files";
import { fixtureBoard, fixtureImage } from "./fixture";
test("storyboard pins exact frame total and Standard provenance", () => { const board = fixtureBoard(); assert.equal(storyboardSchema.parse(board).scenes.length, 2); assert.equal(storyboardSchema.safeParse({ ...board, durationSeconds: 4 }).success, false); assert.equal(storyboardSchema.safeParse({ ...board, scenes: [{ ...board.scenes[0], generationStrategy: "ai_shot" }, board.scenes[1]] }).success, false); });
test("Standard custom framing preserves normalized start and end crops and supported motion presets", () => {
  const board = fixtureBoard();
  const startCrop = { x: 0.1, y: 0.12, width: 0.8, height: 0.76 };
  const endCrop = { x: 0.2, y: 0.18, width: 0.6, height: 0.62 };
  const parsed = storyboardSchema.parse({ ...board, scenes: board.scenes.map((scene, index) => index === 0
    ? { ...scene, crop: startCrop, endCrop, motion: "custom" }
    : { ...scene, motion: "pan_up" }) });
  assert.deepEqual(parsed.scenes[0]?.crop, startCrop);
  assert.deepEqual(parsed.scenes[0]?.endCrop, endCrop);
  assert.equal(parsed.scenes[0]?.motion, "custom");
  assert.equal(parsed.scenes[1]?.motion, "pan_up");
  for (const motion of ["zoom", "pan_left", "pan_up", "custom"])
    assert.equal(storyboardSchema.safeParse({ ...board, scenes: [{ ...board.scenes[0]!, motion }, board.scenes[1]] }).success, true);
  assert.equal(storyboardSchema.safeParse({ ...board, scenes: [{ ...board.scenes[0]!, motion: "spin" }, board.scenes[1]] }).success, false);

  for (const invalidCrop of [
    { x: -0.01, y: 0, width: 0.5, height: 0.5 },
    { x: 0.8, y: 0, width: 0.3, height: 0.5 },
    { x: 0, y: 0.8, width: 0.5, height: 0.3 },
  ]) {
    const invalid = { ...board, scenes: [{ ...board.scenes[0]!, endCrop: invalidCrop }, board.scenes[1]] };
    assert.equal(storyboardSchema.safeParse(invalid).success, false);
  }
});
test("Standard storyboard enforces the two-to-four scene range independently of frame totals", () => {
  const board = fixtureBoard();
  const oneScene = { ...board, scenes: [{ ...board.scenes[0]!, durationFrames: 360 }] };
  const fiveScenes = { ...board, scenes: Array.from({ length: 5 }, (_, index) => ({
    ...board.scenes[0]!, sceneOrder: index, durationFrames: 72,
  })) };
  assert.equal(storyboardSchema.safeParse(oneScene).success, false);
  assert.equal(storyboardSchema.safeParse(fiveScenes).success, false);
});
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
  const fourScenes = Array.from({ length: 4 }, (_, i) => ({ ...base.scenes[0]!, sceneOrder: i, durationFrames: 99, transition: i ? "whip_pan" as const : "none" as const, transitionFrames: i ? 12 : 0 }));
  const standard = { ...base, outputDurationFrames: 360, transitionOverlapFrames: 12, scenes: fourScenes };
  assert.equal(storyboardSchema.safeParse(standard).success, true);
  assert.equal(storyboardSchema.safeParse({ ...standard, outputDurationFrames: 372 }).success, false);
  assert.equal(storyboardSchema.safeParse({ ...standard, scenes: fourScenes.map(scene => ({ ...scene, transitionFrames: 0 })) }).success, false);

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
