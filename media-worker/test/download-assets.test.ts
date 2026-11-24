import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { downloadAssets } from "../src/media-files";
import type { WorkerJob } from "../src/contracts";

const asset = (fileName: string, path = fileName) => ({ mockupId: "00000000-0000-0000-0000-000000000000", fileName,
  url: `https://res.cloudinary.com/example/image/upload/${path}.png?secret=private` });

test("assets are fetched in parallel but entered in the job's order", async () => {
  const original = globalThis.fetch;
  const directory = await mkdtemp(join(tmpdir(), "apcs-download-test-"));
  let running = 0, peak = 0;
  // The first asset answers last, so a map filled in completion order would list it last.
  globalThis.fetch = async input => {
    const name = String(input).match(/upload\/([a-z0-9]+)\.png/)![1]!;
    running++; peak = Math.max(peak, running);
    await new Promise(resolve => setTimeout(resolve, name === "a" ? 60 : 5));
    running--;
    return new Response(name);
  };
  try {
    const job = { assets: ["a", "b", "c", "d", "e", "f"].map(name => asset(name)) } as WorkerJob;
    const downloaded = await downloadAssets(job, directory, AbortSignal.timeout(5000));
    assert.deepEqual([...downloaded.keys()], ["a", "b", "c", "d", "e", "f"]);
    assert.ok(peak > 1 && peak <= 4, `expected 2 to 4 downloads at once, saw ${peak}`);
    for (const [name, saved] of downloaded) {
      assert.equal(saved.data.toString(), name);
      assert.equal((await readFile(saved.file)).toString(), name);
    }
  } finally { globalThis.fetch = original; await rm(directory, { recursive: true, force: true }); }
});

test("an untrusted host is refused before anything is fetched", async () => {
  const original = globalThis.fetch;
  let fetched = 0;
  globalThis.fetch = async () => { fetched++; return new Response("x"); };
  try {
    const job = { assets: [asset("a"), { mockupId: "00000000-0000-0000-0000-000000000000", fileName: "b", url: "https://example.com/b.png" }] } as WorkerJob;
    await assert.rejects(downloadAssets(job, ".", AbortSignal.timeout(1000)), /Untrusted media host/);
    assert.equal(fetched, 0);
  } finally { globalThis.fetch = original; }
});

test("the first failed download stops the others and is the error reported", async () => {
  const original = globalThis.fetch;
  const directory = await mkdtemp(join(tmpdir(), "apcs-download-test-"));
  globalThis.fetch = async (input, init) => {
    const name = String(input).match(/upload\/([a-z0-9]+)\.png/)![1]!;
    if (name === "b") return new Response(null, { status: 404 });
    await new Promise((resolve, reject) => {
      const timer = setTimeout(resolve, 2000);
      init?.signal?.addEventListener("abort", () => { clearTimeout(timer); reject(new Error("aborted")); }, { once: true });
    });
    return new Response(name);
  };
  try {
    const job = { assets: [asset("a"), asset("b"), asset("c")] } as WorkerJob;
    const started = Date.now();
    await assert.rejects(downloadAssets(job, directory, AbortSignal.timeout(5000)), /Could not download b \(HTTP 404\)/);
    assert.ok(Date.now() - started < 1500, "the other downloads should have been cancelled");
  } finally { globalThis.fetch = original; await rm(directory, { recursive: true, force: true }); }
});
