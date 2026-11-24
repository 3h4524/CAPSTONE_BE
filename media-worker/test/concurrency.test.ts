import assert from "node:assert/strict";
import { test } from "node:test";
import { mapWithConcurrency, readExportSceneConcurrency } from "../src/concurrency";

test("mapWithConcurrency keeps the order of the items and never runs more than the limit", async () => {
  let running = 0, peak = 0;
  const results = await mapWithConcurrency([5, 1, 4, 2, 3], 2, async item => {
    peak = Math.max(peak, ++running);
    await new Promise(done => setTimeout(done, item * 5));
    running--;
    return item * 10;
  });
  assert.deepEqual(results, [50, 10, 40, 20, 30]);
  assert.equal(peak, 2);
});

test("mapWithConcurrency handles no items and a limit larger than the items", async () => {
  assert.deepEqual(await mapWithConcurrency([], 3, async () => 1), []);
  assert.deepEqual(await mapWithConcurrency([1, 2], 9, async item => item + 1), [2, 3]);
  assert.deepEqual(await mapWithConcurrency([1, 2], 0, async item => item + 1), [2, 3], "a limit below one still runs the items");
});

test("mapWithConcurrency throws the first error after the running ones finish, and starts nothing more", async () => {
  const started: number[] = [];
  let finished = 0;
  await assert.rejects(mapWithConcurrency([0, 1, 2, 3, 4], 2, async item => {
    started.push(item);
    await new Promise(done => setTimeout(done, 10));
    finished++;
    if (item === 0) throw new Error("first");
    return item;
  }), /first/);
  assert.equal(finished, started.length);
  assert.ok(started.length < 5);
});

test("the scenes of an export render one at a time on a small host and two on a larger one, unless it is set", () => {
  // Two tabs for each scene: two cores fit one scene, four cores fit two.
  assert.equal(readExportSceneConcurrency(undefined, 1, 2), 1);
  assert.equal(readExportSceneConcurrency(undefined, 2, 2), 1);
  assert.equal(readExportSceneConcurrency(undefined, 4, 2), 2);
  assert.equal(readExportSceneConcurrency(undefined, 16, 2), 2, "never more than two without being asked");
  assert.equal(readExportSceneConcurrency("3", 2, 2), 3);
  assert.equal(readExportSceneConcurrency("9", 2, 2), 4);
  for (const value of ["", "0", "-1", "two", "1.5"])
    assert.equal(readExportSceneConcurrency(value, 4, 2), 2, `'${value}' falls back to the default`);
});
