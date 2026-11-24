import assert from "node:assert/strict";
import { test } from "node:test";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { packageBatchExport, type ExportProduct, type SceneRenderer } from "../src/batch-export";
import { fixtureBoard, fixtureImage } from "./fixture";

const id = "fdc00975-356a-4340-857f-a1fa2c273241";

/** A package of four scenes: the product, its snapshot and the directory the files are in. */
async function fourScenes(withFinalVideo: boolean) {
  const directory = await mkdtemp(join(tmpdir(), "apcs-batch-parallel-"));
  const image = fixtureImage();
  const base = fixtureBoard("tshirt", image, 12);
  const board = { ...base, scenes: [0, 1, 2, 3].map(index => ({ ...base.scenes[index % 2]!, sceneOrder: index, durationFrames: 90 })) };
  const input = join(directory, "input.png");
  const videoFile = join(directory, "video.mp4");
  await writeFile(input, image);
  await writeFile(videoFile, "final");
  const snapshot = { videoId: id, storyboardFingerprint: board.fingerprint, scenes: board.scenes.map(scene => ({
    sceneOrder: scene.sceneOrder, mockupId: scene.mockupId, sourceRevision: scene.sourceRevision,
    sourceHash: scene.sourceHash, imageFile: input, imageVersion: "1",
  })) };
  const product: ExportProduct = { id, name: "Product", type: "tshirt", images: [{ id: "mockup-1", file: input, path: "mockup/mockup-01.png" }],
    videos: [{ id, version: 1, storageVersion: "1", includeScenes: true, storyboard: board, snapshot,
      ...(withFinalVideo ? { file: videoFile, thumbnailFile: input, thumbnailVersion: "1" } : {}) }] };
  return { directory, product, input };
}

/** The scene files named in a package, in the order they were written. */
const sceneEntries = (zip: Buffer) =>
  // A name is in the file twice, at the entry and in the directory at the end: once is enough.
  [...new Set([...zip.toString("latin1").matchAll(/video\/scenes\/[^/]+\/(scene-0\d\.mp4)/g)].map(match => match[1]!))];

async function renderFourScenes(sceneConcurrency: number | undefined, timings?: Record<string, number>) {
  const { directory, product } = await fourScenes(true);
  try {
    let running = 0, peak = 0;
    const renderer: SceneRenderer = { async renderScene(props, target) {
      peak = Math.max(peak, ++running);
      // The first scene is the slowest, so with scenes side by side it finishes last.
      await new Promise(done => setTimeout(done, props.sceneOrder === 0 ? 80 : 20));
      running--;
      const file = join(target, `scene-${props.sceneOrder}.mp4`);
      await writeFile(file, `scene ${props.sceneOrder}`);
      return { video: file, timings: { frames: 1.5, mux: 0.5 } };
    } };
    const parts = await packageBatchExport(directory, [product], renderer, 4 * 1024 * 1024, AbortSignal.timeout(10_000), { sceneConcurrency, timings });
    return { peak, zip: await readFile(parts[0]!) };
  } finally { await rm(directory, { recursive: true, force: true }); }
}

test("batch export renders scenes side by side within the limit and still packs them in scene order", async () => {
  const sequential = await renderFourScenes(undefined);
  const parallel = await renderFourScenes(3);
  assert.equal(sequential.peak, 1, "one at a time unless asked otherwise");
  assert.equal(parallel.peak, 3);
  // The same package either way: the entries are written in scene order, not in the order the renders finished.
  assert.deepEqual(sceneEntries(parallel.zip), ["scene-01.mp4", "scene-02.mp4", "scene-03.mp4", "scene-04.mp4"]);
  assert.deepEqual(sceneEntries(parallel.zip), sceneEntries(sequential.zip));
});

test("batch export reports where its time went: scenes and ZIP as time passed, each scene step summed", async () => {
  const timings: Record<string, number> = {};
  await renderFourScenes(2, timings);
  assert.ok(timings.scenes! > 0, "the time the scenes took");
  assert.ok(timings.zip! >= 0, "the time the ZIP took");
  assert.equal(timings.sceneFrames, 6, "four scenes of 1.5 s");
  assert.equal(timings.sceneMux, 2, "four scenes of 0.5 s");
  // Each scene on its own, named by its number and how it was made (the fixture's scenes are photo motion).
  for (const name of ["scene01Photo", "scene02Photo", "scene03Photo", "scene04Photo"]) assert.ok(timings[name] !== undefined, name);
  assert.ok(timings.scene01Photo! > timings.scene02Photo!, "the first scene is the slow one");
});

test("batch export stops starting scenes once one fails, and waits for those already running before it throws", async () => {
  const { directory, product, input } = await fourScenes(false);
  try {
    const started: number[] = [];
    let finished = 0;
    const renderer: SceneRenderer = { async renderScene(props) {
      started.push(props.sceneOrder);
      await new Promise(done => setTimeout(done, 20));
      finished++;
      if (props.sceneOrder === 0) throw new Error("Render failed");
      return { video: input };
    } };
    await assert.rejects(packageBatchExport(directory, [product], renderer, 4 * 1024 * 1024, AbortSignal.timeout(10_000), { sceneConcurrency: 2 }), /Render failed/);
    assert.equal(finished, started.length, "nothing is still running when it throws, so its directory can be removed");
    assert.ok(started.length < 4, "scenes after the failure are not started");
  } finally { await rm(directory, { recursive: true, force: true }); }
});
