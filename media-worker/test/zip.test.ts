import assert from "node:assert/strict";
import { test } from "node:test";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve, sep } from "node:path";
import { inflateRawSync } from "node:zlib";
import { createZip } from "../src/media-files";

const id = "fdc00975-356a-4340-857f-a1fa2c273241";
const manifest = { schemaVersion: 1, productId: id, videoId: id, version: 2, mode: "standard", fingerprint: "a".repeat(64), approvedRevision: 3, video: "video.mp4", thumbnail: "thumbnail.png", scenes: [{ order: 0, mockupId: id, sourceRevision: 1, sourceHash: "b".repeat(64), generationStrategy: "standard", source: `mockups/${id}.png` }] };
test("ZIP contains exact approved media entries and sanitized provenance", async () => {
  const directory = await mkdtemp(join(tmpdir(), "apcs-zip-test-"));
  try {
    const source = join(directory, "source"); await writeFile(source, "fixture bytes");
    const zipFile = join(directory, "package.zip");
    await createZip(zipFile, new Map(["video.mp4", "thumbnail.png", `mockups/${id}.png`].map(name => [name, { file: source, data: Buffer.from("fixture bytes") }])), manifest, AbortSignal.timeout(5000));
    const zip = await readFile(zipFile); const names: string[] = [];
    let extractedManifest: unknown;
    for (let offset = 0; offset < zip.length - 46; offset++) {
      if (zip.readUInt32LE(offset) !== 0x02014b50) continue;
      const length = zip.readUInt16LE(offset + 28); const name = zip.subarray(offset + 46, offset + 46 + length).toString(); names.push(name);
      if (name !== "manifest.json") continue;
      const local = zip.readUInt32LE(offset + 42); const start = local + 30 + zip.readUInt16LE(local + 26) + zip.readUInt16LE(local + 28);
      const compressed = zip.subarray(start, start + zip.readUInt32LE(offset + 20));
      extractedManifest = JSON.parse(inflateRawSync(compressed).toString());
    }
    assert.deepEqual(names.sort(), ["video.mp4", "thumbnail.png", `mockups/${id}.png`, "manifest.json"].sort());
    assert.deepEqual(extractedManifest, manifest);
  } finally { if (resolve(directory).startsWith(resolve(tmpdir()) + sep)) await rm(directory, { recursive: true, force: true }); }
});
test("ZIP refuses path traversal and sensitive manifest fields before creating a file", async () => {
  await assert.rejects(createZip("unused.zip", new Map([["../secret", { file: "unused", data: Buffer.alloc(0) }]]), manifest, AbortSignal.timeout(5000)), /Unsafe ZIP/);
  await assert.rejects(createZip("unused.zip", new Map(), { ...manifest, signedUrl: "secret" }, AbortSignal.timeout(5000)));
});
