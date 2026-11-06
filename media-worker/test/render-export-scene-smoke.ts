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
  outputDurationFrames: 180, scenes: [{ ...scene, sceneOrder: 0 }, { ...scene, sceneOrder: 1 }],
  videoEdit: { version: 1 as const, music: { trackId: "fdc00975-356a-4340-857f-a1fa2c273241", startFrame: 0, sourceOffsetFrames: 0, volume: .5, fadeInFrames: 0, fadeOutFrames: 0, loop: false, muted: false }, overlays: [] },
  aiShots: [0, 1].map(sceneOrder => ({ sceneOrder, mockupId: scene.mockupId, sourceHash: scene.sourceHash, model: "MiniMax-H3" as const,
    preset: "product_cinematic" as const, prompt: "Synthetic scene export", durationSeconds: 4, fingerprint: "b".repeat(64) })) };
const result = await new RemotionVideoRenderer().renderScene({ storyboard: board, sceneOrder: 1,
  sources: { [scene.mockupId]: pngSource(image, scene.sourceHash) }, clips: { 1: clip }, finalVideo: clip }, directory, AbortSignal.timeout(300_000));
assert.equal(result.qa.width, 1080);
assert.equal(result.qa.height, 1080);
assert.equal(result.qa.hasAudio, true);
assert.ok(Math.abs(result.qa.durationSeconds - 3) < .04);
process.stdout.write("Scene export smoke: 1080x1080, 3 seconds, audio verified.\n");
