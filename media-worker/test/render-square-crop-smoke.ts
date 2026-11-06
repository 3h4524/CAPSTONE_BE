import assert from "node:assert/strict";
import { mkdir, readFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import ffmpeg from "ffmpeg-static";
import ffprobe from "ffprobe-static";
import { RemotionVideoRenderer } from "../src/renderer";
import { processOutput } from "../src/process";
import { pngSource } from "../src/media-files";
import { fixtureBoard, fixtureImage } from "./fixture";

process.env.FFMPEG_PATH ??= ffmpeg!;
process.env.FFPROBE_PATH ??= ffprobe.path;
const directory = resolve("artifacts", "square-crop-smoke");
await mkdir(directory, { recursive: true });
const clip = join(directory, "source.mp4");
await processOutput(process.env.FFMPEG_PATH, ["-y", "-f", "lavfi", "-i", "color=red:s=720x1280:r=30:d=4,drawbox=x=0:y=640:w=720:h=640:color=blue:t=fill", "-pix_fmt", "yuv420p", "-c:v", "libx264", clip], AbortSignal.timeout(120_000));
const image = fixtureImage();
const base = fixtureBoard("tshirt", image, 6);
const sourceScene = base.scenes[0]!;
const scene = { ...sourceScene, durationFrames: 90, text: "", generationStrategy: "ai_shot" as const, transition: "none" as const };
const board = { ...base, outputFormat: "square" as const, width: 1080, height: 1080, aspectRatio: "1:1", mode: "ai_shot" as const,
  outputDurationFrames: 180, scenes: [{ ...scene, sceneOrder: 0, aiCropPosition: { x: .5, y: 0 } }, { ...scene, sceneOrder: 1, aiCropPosition: { x: .5, y: 1 } }],
  aiShots: [0, 1].map(sceneOrder => ({ sceneOrder, mockupId: scene.mockupId, sourceHash: scene.sourceHash, model: "MiniMax-H3" as const,
    preset: "product_cinematic" as const, prompt: "Synthetic crop test", durationSeconds: 4, fingerprint: "b".repeat(64) })) };
const result = await new RemotionVideoRenderer().render({ storyboard: board, sources: { [scene.mockupId]: pngSource(image, scene.sourceHash) }, clips: { 0: clip, 1: clip } }, directory, AbortSignal.timeout(300_000), () => {});
for (const [name, second] of [["top", 1], ["bottom", 4]] as const) {
  await processOutput(process.env.FFMPEG_PATH, ["-y", "-ss", String(second), "-i", result.video, "-vf", "crop=100:100:490:490,scale=1:1", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", join(directory, `${name}.rgb`)], AbortSignal.timeout(30_000));
}
const top = await readFile(join(directory, "top.rgb"));
const bottom = await readFile(join(directory, "bottom.rgb"));
assert.ok(top[0]! > top[2]! + 100, `Top crop should show red, got ${[...top]}`);
assert.ok(bottom[2]! > bottom[0]! + 100, `Bottom crop should show blue, got ${[...bottom]}`);
assert.equal(result.qa.width, 1080);
assert.equal(result.qa.height, 1080);
process.stdout.write("Square AI crop render: top and bottom framing verified.\n");
