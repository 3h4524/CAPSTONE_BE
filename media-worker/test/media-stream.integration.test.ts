import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { setTimeout as delay } from "node:timers/promises";
import { test } from "node:test";
import { createClient } from "redis";
import { runMediaStream, type MediaStreamEvent } from "../src/media-stream";

const redisUrl = process.env.APCS_TEST_REDIS_URL;

test("Redis stream waits, acknowledges settled jobs, dead-letters invalid entries and recovers pending jobs", { skip: !redisUrl, timeout: 30_000 }, async () => {
  const client = createClient({ url: redisUrl });
  client.on("error", () => {});
  await client.connect();
  const stream = `apcs:test:media:${randomUUID()}`;
  const originalUrl = process.env.MEDIA_QUEUE_REDIS_URL;
  const originalStream = process.env.MEDIA_QUEUE_STREAM;
  process.env.MEDIA_QUEUE_REDIS_URL = redisUrl;
  process.env.MEDIA_QUEUE_STREAM = stream;
  const seen: MediaStreamEvent[] = [];
  let settle = true;
  let abort = new AbortController();
  let worker = runMediaStream(abort.signal, async (event) => { seen.push(event); return settle; });
  const fields = () => ({ schemaVersion: "1", eventId: randomUUID(), jobId: randomUUID(), kind: "render", queueVersion: "1" });
  const waitFor = async (condition: () => Promise<boolean>) => {
    const deadline = Date.now() + 5000;
    while (Date.now() < deadline) {
      if (await condition()) return;
      await delay(25);
    }
    throw new Error("Timed out waiting for Redis stream state");
  };
  try {
    await waitFor(async () => (await client.exists(stream)) === 1);
    await delay(250);
    assert.equal(seen.length, 0, "idle worker must not call the handler");

    const valid = fields();
    await client.xAdd(stream, "*", valid);
    await waitFor(async () => seen.length === 1 && (await client.xLen(stream)) === 0);
    assert.equal(seen[0]?.jobId, valid.jobId);
    assert.equal(seen[0]?.queueVersion, 1);
    assert.equal((await client.sendCommand(["XPENDING", stream, "media-workers"]) as unknown[])[0], 0);

    const invalidId = await client.xAdd(stream, "*", { schemaVersion: "2", eventId: "invalid" });
    await waitFor(async () => (await client.xLen(`${stream}:dead`)) === 1);
    const dead = await client.xRange(`${stream}:dead`, "-", "+");
    assert.equal(dead[0]?.message.originalId, invalidId);
    assert.equal(dead[0]?.message.reason, "invalid_message");
    assert.equal(seen.length, 1, "invalid entries must never reach the handler");
    assert.equal((await client.sendCommand(["XPENDING", stream, "media-workers"]) as unknown[])[0], 0);

    settle = false;
    const pending = fields();
    const pendingId = await client.xAdd(stream, "*", pending);
    await waitFor(async () => seen.length === 2);
    assert.equal((await client.sendCommand(["XPENDING", stream, "media-workers"]) as unknown[])[0], 1);
    assert.equal(await client.xLen(stream), 1);

    abort.abort();
    await worker;
    // Move the pending delivery into another consumer and age its idle time without a two-minute test delay.
    await client.sendCommand(["XCLAIM", stream, "media-workers", "abandoned", "0", pendingId, "IDLE", "130001"]);
    settle = true;
    abort = new AbortController();
    worker = runMediaStream(abort.signal, async (event) => { seen.push(event); return settle; });
    await waitFor(async () => seen.length === 3 && (await client.xLen(stream)) === 0);
    assert.equal(seen[2]?.jobId, pending.jobId);
    assert.equal((await client.sendCommand(["XPENDING", stream, "media-workers"]) as unknown[])[0], 0);

    await client.del(`${stream}:dead`);
    await client.set(`${stream}:dead`, "wrong-type");
    const failedDeadLetter = await client.xAdd(stream, "*", { schemaVersion: "2", eventId: "invalid" });
    await waitFor(async () => (await client.sendCommand(["XPENDING", stream, "media-workers"]) as unknown[])[0] === 1);
    await delay(250);
    assert.equal((await client.sendCommand(["XPENDING", stream, "media-workers"]) as unknown[])[0], 1, "failed dead-letter writes must leave the message pending");
    assert.equal((await client.xRange(stream, failedDeadLetter, failedDeadLetter)).length, 1, "failed dead-letter writes must not delete the message");
    assert.equal(await client.get(`${stream}:dead`), "wrong-type");
  } finally {
    abort.abort();
    await worker;
    await client.del([stream, `${stream}:dead`]);
    await client.quit();
    if (originalUrl === undefined) delete process.env.MEDIA_QUEUE_REDIS_URL; else process.env.MEDIA_QUEUE_REDIS_URL = originalUrl;
    if (originalStream === undefined) delete process.env.MEDIA_QUEUE_STREAM; else process.env.MEDIA_QUEUE_STREAM = originalStream;
  }
});
