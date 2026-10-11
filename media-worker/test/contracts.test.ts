import assert from "node:assert/strict";
import { test } from "node:test";
import { exportPayloadSchema, storyboardSchema } from "../src/contracts";
import { pngSource } from "../src/media-files";
import { fixtureBoard, fixtureImage } from "./fixture";
test("storyboard pins exact frame total and Standard provenance", () => { const board = fixtureBoard(); assert.equal(storyboardSchema.parse(board).scenes.length, 2); assert.equal(storyboardSchema.safeParse({ ...board, durationSeconds: 4 }).success, false); assert.equal(storyboardSchema.safeParse({ ...board, scenes: [{ ...board.scenes[0], generationStrategy: "ai_shot" }, board.scenes[1]] }).success, false); });
test("immutable source verifies hash and PNG dimensions", () => { const image = fixtureImage(); const board = fixtureBoard("mug", image); const source = pngSource(image, board.scenes[0]!.sourceHash); assert.equal(source.width, 540); assert.equal(source.height, 1080); assert.throws(() => pngSource(image, "b".repeat(64)), /hash/); });
test("ZIP manifest rejects secrets or provider metadata", () => {
  const manifest = { schemaVersion: 1, productId: "fdc00975-356a-4340-857f-a1fa2c273241", videoId: "fdc00975-356a-4340-857f-a1fa2c273241", version: 1, mode: "standard", fingerprint: "a".repeat(64), approvedRevision: 2, video: "video.mp4", thumbnail: "thumbnail.png", scenes: [] };
  const payload = { exportId: manifest.videoId, videoId: manifest.videoId, manifest };
  assert.equal(exportPayloadSchema.safeParse(payload).success, true);
  assert.equal(exportPayloadSchema.safeParse({ ...payload, manifest: { ...manifest, prompt: "secret", signedUrl: "https://example.com" } }).success, false);
});
