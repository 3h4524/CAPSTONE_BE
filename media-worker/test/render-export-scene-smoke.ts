import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import { join, resolve } from "node:path";
import ffmpeg from "ffmpeg-static";
import ffprobe from "ffprobe-static";
import { RemotionVideoRenderer } from "../src/renderer";
import { processOutput } from "../src/process";
import { pngSource } from "../src/media-files";
import { fixtureBoard, fixtureImage } from "./fixture";

process.env.FFMPEG_PATH ??= ffmpeg!;
process.env.FFPROBE_PATH ??= ffprobe.path;
const directory = resolve("artifacts", "export-scene-smoke");
await mkdir(directory, { recursive: true });
const clip = join(directory, "source.mp4");
await processOutput(process.env.FFMPEG_PATH, ["-y", "-f", "lavfi", "-i", "color=red:s=720x1280:r=30:d=6", "-f", "lavfi", "-i", "sine=frequency=440:duration=6", "-shortest", "-pix_fmt", "yuv420p", "-c:v", "libx264", "-c:a", "aac", clip], AbortSignal.timeout(120_000));
const image = fixtureImage();
const base = fixtureBoard("tshirt", image, 6);
const scene = { ...base.scenes[0]!, durationFrames: 90, text: "", generationStrategy: "ai_shot" as const, transition: "none" as const };
const board = { ...base, outputFormat: "square" as const, width: 1080, height: 1080, aspectRatio: "1:1", mode: "ai_shot" as const,
  // Scene 1 carries a caption, so it is drawn in the browser; scene 2 has no text, so it is cut from its clip with FFmpeg.
  outputDurationFrames: 180, scenes: [{ ...scene, sceneOrder: 0, text: "Soft cotton" }, { ...scene, sceneOrder: 1 }],
  videoEdit: { version: 1 as const, music: { trackId: "fdc00975-356a-4340-857f-a1fa2c273241", startFrame: 0, sourceOffsetFrames: 0, volume: .5, fadeInFrames: 0, fadeOutFrames: 0, loop: false, muted: false }, overlays: [] },
  aiShots: [0, 1].map(sceneOrder => ({ sceneOrder, mockupId: scene.mockupId, sourceHash: scene.sourceHash, model: "MiniMax-H3" as const,
    preset: "product_cinematic" as const, prompt: "Synthetic scene export", durationSeconds: 4, fingerprint: "b".repeat(64) })) };
const renderer = new RemotionVideoRenderer();
const sources = { [scene.mockupId]: pngSource(image, scene.sourceHash) };
for (const sceneOrder of [0, 1]) {
  const result = await renderer.renderScene({ storyboard: board, sceneOrder, sources, clips: { 0: clip, 1: clip } }, directory, AbortSignal.timeout(300_000));
  assert.equal(result.cutFrom, sceneOrder === 1 ? "clip" : null, sceneOrder === 1 ? "the scene with no text is cut from its clip" : "the captioned scene is drawn");
  assert.equal(result.qa.width, 1080);
  assert.equal(result.qa.height, 1080);
  assert.equal(result.qa.hasAudio, false, "a scene has no sound, even when the video has music");
  assert.ok(Math.abs(result.qa.durationSeconds - 3) < .04);
  process.stdout.write(`Scene ${sceneOrder + 1}: ${JSON.stringify(result.timings)}\n`);
}
process.stdout.write("Scene export smoke: 1080x1080, 3 seconds, no sound; drawn and cut scenes verified.\n");
process.exit(0);
