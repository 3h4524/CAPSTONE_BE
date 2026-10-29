import { mkdir, writeFile } from "node:fs/promises";
import { resolve, join } from "node:path";
import ffmpeg from "ffmpeg-static";
import ffprobe from "ffprobe-static";
import { RemotionVideoRenderer } from "../src/renderer";
import { processOutput } from "../src/process";
import { fixtureBoard, fixtureImage } from "./fixture";
import { pngSource } from "../src/media-files";

process.env.FFMPEG_PATH ??= ffmpeg!;
process.env.FFPROBE_PATH ??= ffprobe.path;
const directory = resolve("artifacts", "ai-shot-smoke");
await mkdir(directory, { recursive: true });
const image = fixtureImage();
const sourceImage = join(directory, "source.png");
const clip = join(directory, "synthetic-ai.mp4");
await writeFile(sourceImage, image);
await processOutput(process.env.FFMPEG_PATH, ["-y", "-loop", "1", "-i", sourceImage, "-t", "8", "-vf", "scale=720:1280", "-r", "24", "-pix_fmt", "yuv420p", "-c:v", "libx264", clip], AbortSignal.timeout(120_000));
const base = fixtureBoard("tshirt", image, 8);
const scene = { ...base.scenes[0]!, sceneOrder: 0, durationFrames: 240, text: "", generationStrategy: "ai_shot" as const };
const board = { ...base, mode: "ai_shot" as const, scenes: [scene], aiShots: [{ sceneOrder: 0, mockupId: scene.mockupId, sourceHash: scene.sourceHash, model: "veo-3.1-lite-generate-preview" as const, preset: "product_cinematic" as const, prompt: "Synthetic smoke test", durationSeconds: 8 as const, fingerprint: "b".repeat(64) }] };
const renderer = new RemotionVideoRenderer();
const result = await renderer.render({ storyboard: board, sources: { [scene.mockupId]: pngSource(image, scene.sourceHash) }, clips: { 0: clip } }, directory, AbortSignal.timeout(240_000), () => {});
process.stdout.write(`AI Shot smoke: ${JSON.stringify(result.qa)}\n`);
if (process.argv.includes("--all")) {
  const source = pngSource(image, scene.sourceHash);
  const mixedDirectory = join(directory, "mixed");
  await mkdir(mixedDirectory, { recursive: true });
  const mixedBase = fixtureBoard("tshirt", image, 12);
  const mixed = { ...mixedBase, mode: "ai_shot" as const, outputDurationFrames: 360, transitionOverlapFrames: 12,
    aiShots: [{ ...board.aiShots[0]!, durationSeconds: 6 as const }],
    scenes: [{ ...mixedBase.scenes[0]!, generationStrategy: "ai_shot" as const, durationFrames: 180, transition: "none" as const },
      { ...mixedBase.scenes[1]!, durationFrames: 180, transition: "fade" as const }] };
  const mixedResult = await renderer.render({ storyboard: mixed, sources: { [scene.mockupId]: source }, clips: { 0: clip } }, mixedDirectory, AbortSignal.timeout(300_000), () => {});
  process.stdout.write(`Mixed AI+Standard smoke: ${JSON.stringify(mixedResult.qa)}\n`);
  const fullDirectory = join(directory, "full-15");
  await mkdir(fullDirectory, { recursive: true });
  const fullBase = fixtureBoard("tshirt", image, 15);
  const full = { ...fullBase, mode: "ai_shot" as const, outputDurationFrames: 480, transitionOverlapFrames: 12,
    aiShots: [{ ...board.aiShots[0]! }, { ...board.aiShots[0]!, sceneOrder: 1, fingerprint: "c".repeat(64) }],
    scenes: [{ ...fullBase.scenes[0]!, generationStrategy: "ai_shot" as const, durationFrames: 240, transition: "none" as const },
      { ...fullBase.scenes[1]!, generationStrategy: "ai_shot" as const, durationFrames: 240, transition: "fade" as const }] };
  const fullResult = await renderer.render({ storyboard: full, sources: { [scene.mockupId]: source }, clips: { 0: clip, 1: clip } }, fullDirectory, AbortSignal.timeout(360_000), () => {});
  process.stdout.write(`Full 15-second AI smoke: ${JSON.stringify(fullResult.qa)}\n`);
}
