import { mkdir, writeFile } from "node:fs/promises";
import { resolve, join } from "node:path";
import ffmpeg from "ffmpeg-static";
import ffprobe from "ffprobe-static";
import { RemotionVideoRenderer } from "../src/renderer";
import { pngSource } from "../src/media-files";
import { fixtureBoard, fixtureImage } from "./fixture";
process.env.FFMPEG_PATH ??= ffmpeg!;
process.env.FFPROBE_PATH ??= ffprobe.path;
const renderer = new RemotionVideoRenderer();
const types = process.argv.includes("--all") ? ["tshirt", "hoodie", "mug", "poster", "tote_bag", "phone_case"] : ["tshirt"];
for (const [index, type] of types.entries()) {
  const directory = resolve("artifacts", type); await mkdir(directory, { recursive: true });
  const image = fixtureImage(index); const board = fixtureBoard(type, image);
  await writeFile(join(directory, "source.png"), image);
  const source = pngSource(image, board.scenes[0]!.sourceHash);
  const rendered = await renderer.render({ storyboard: board, sources: { [board.scenes[0]!.mockupId]: source } }, directory, AbortSignal.timeout(240_000), () => {});
  await writeFile(join(directory, "qa.json"), JSON.stringify(rendered.qa, null, 2));
  process.stdout.write(`${type}: ${JSON.stringify(rendered.qa)}\n`);
}
