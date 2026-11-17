import assert from "node:assert/strict";
import { test } from "node:test";
import { followAiShot, readAiShotPollingSettings, type AiShotPollingSettings } from "../src/ai-shot-polling";

const settings: AiShotPollingSettings = { firstPollMs: 30_000, intervalMs: 10_000, timeoutMs: 25 * 60_000 };

/** A clock that only moves when the code under test waits. */
function harness(answers: string[], restart: string[] = []) {
  let clock = 0;
  const waits: number[] = [];
  const calls: string[] = [];
  const queue = [...answers];
  const restarts = [...restart];
  return {
    waits, calls,
    operations: {
      start: async () => { calls.push("start"); return { status: restarts.shift() ?? "polling" }; },
      poll: async () => { calls.push("poll"); return { status: queue.shift() ?? "polling" }; },
      delay: async (ms: number) => { waits.push(ms); clock += ms; },
      now: () => clock
    }
  };
}

test("defaults wait 30 seconds, then ask every 10 seconds, for at most 25 minutes", () => {
  assert.deepEqual(readAiShotPollingSettings({}), { firstPollMs: 30_000, intervalMs: 10_000, timeoutMs: 25 * 60_000 });
});

test("settings come from the environment, hold to their limits and ignore what is not a whole number", () => {
  assert.deepEqual(
    readAiShotPollingSettings({ AI_SHOT_FIRST_POLL_SECONDS: "0", AI_SHOT_POLL_SECONDS: "15", AI_SHOT_TIMEOUT_MINUTES: "20" }),
    { firstPollMs: 0, intervalMs: 15_000, timeoutMs: 20 * 60_000 });
  assert.deepEqual(
    readAiShotPollingSettings({ AI_SHOT_FIRST_POLL_SECONDS: "999", AI_SHOT_POLL_SECONDS: "1", AI_SHOT_TIMEOUT_MINUTES: "60" }),
    { firstPollMs: 120_000, intervalMs: 5_000, timeoutMs: 28 * 60_000 });
  assert.deepEqual(
    readAiShotPollingSettings({ AI_SHOT_FIRST_POLL_SECONDS: "soon", AI_SHOT_POLL_SECONDS: "2.5", AI_SHOT_TIMEOUT_MINUTES: " " }),
    { firstPollMs: 30_000, intervalMs: 10_000, timeoutMs: 25 * 60_000 });
});

test("the first check waits longer than the ones after it", async () => {
  const { operations, waits, calls } = harness(["polling", "polling", "ready"]);
  const result = await followAiShot({ status: "polling" }, operations, settings);
  assert.equal(result.status, "ready");
  assert.deepEqual(waits, [30_000, 10_000, 10_000]);
  assert.deepEqual(calls, ["poll", "poll", "poll"]);
});

test("a scene that is already ready is not asked about", async () => {
  const { operations, calls } = harness([]);
  assert.equal((await followAiShot({ status: "ready" }, operations, settings)).status, "ready");
  assert.deepEqual(calls, []);
});

test("a failure stops the wait with the provider's state", async () => {
  const { operations } = harness(["failed"]);
  assert.equal((await followAiShot({ status: "polling" }, operations, settings)).status, "failed");
});

test("a scene still being made when the time is up is given up, without asking after the limit", async () => {
  const { operations, waits } = harness([]);
  const result = await followAiShot({ status: "polling" }, operations, settings);
  assert.equal(result.status, "polling");
  assert.equal(waits.reduce((total, wait) => total + wait, 0), settings.timeoutMs);
});

test("a clip the provider dropped for free is sent again and the first check waits as long as before", async () => {
  const { operations, waits, calls } = harness(["ready"], ["polling"]);
  const result = await followAiShot({ status: "pending" }, operations, settings);
  assert.equal(result.status, "ready");
  assert.deepEqual(calls, ["start", "poll"]);
  assert.deepEqual(waits, [30_000]);
});

test("sending the scene again does not extend the time limit", async () => {
  const { operations, waits } = harness([], ["polling"]);
  await followAiShot({ status: "pending" }, operations, settings);
  assert.equal(waits.reduce((total, wait) => total + wait, 0), settings.timeoutMs);
});
