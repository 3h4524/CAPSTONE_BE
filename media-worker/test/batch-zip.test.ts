import assert from "node:assert/strict";
import { test } from "node:test";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createHash } from "node:crypto";
import { createBatchZipParts, type BatchZipProduct } from "../src/batch-zip";

const ids = ["fdc00975-356a-4340-857f-a1fa2c273241", "51200975-356a-4340-857f-a1fa2c273241"];
function entries(zip: Buffer) {
  const result = new Map<string, Buffer>();
  for (let i = 0; i < zip.length - 46; i++) {
    if (zip.readUInt32LE(i) !== 0x02014b50) continue;
    const name = zip.subarray(i + 46, i + 46 + zip.readUInt16LE(i + 28)).toString();
    const local = zip.readUInt32LE(i + 42);
    const start = local + 30 + zip.readUInt16LE(local + 26) + zip.readUInt16LE(local + 28);
    result.set(name, zip.subarray(start, start + zip.readUInt32LE(i + 20)));
  }
  return result;
}

test("batch ZIP keeps product folders, original bytes and UTF-8 listing content", async () => {
  const directory = await mkdtemp(join(tmpdir(), "apcs-batch-zip-"));
  try {
    const design = join(directory, "design");
    await writeFile(design, Buffer.from([0, 1, 2, 255]));
    const products: BatchZipProduct[] = [
      { id: ids[0]!, name: "Áo / xanh", type: "tshirt", assets: [{ id: "d1", path: "design/design-01.png", file: design }], listing: { title: "Áo mùa hè", description: "Dòng 1\nDòng 2", tags: ["áo", "xanh"], version: 3 } },
      { id: ids[1]!, name: "Áo / xanh", type: "tshirt", assets: [{ id: "d2", path: "mockup/mockup-01.png", file: design }] },
    ];
    const [path] = await createBatchZipParts(directory, products, 1024 * 1024, AbortSignal.timeout(10_000));
    const files = entries(await readFile(path!));
    const folder = `Áo-xanh__${ids[0]}`;
    assert.deepEqual(files.get(`${folder}/design/design-01.png`), Buffer.from([0, 1, 2, 255]));
    assert.equal(files.get(`${folder}/listing/description.txt`)?.toString(), "Dòng 1\nDòng 2");
    assert.deepEqual(JSON.parse(files.get(`${folder}/listing/listing.json`)!.toString()).tags, ["áo", "xanh"]);
    assert.ok(files.has(`${folder}/video/`));
    assert.ok(files.has(`Áo-xanh__${ids[1]}/mockup/mockup-01.png`));
    const manifest = JSON.parse(files.get(`${folder}/manifest.json`)!.toString());
    assert.deepEqual(manifest.missing, ["mockup", "video"]);
    assert.equal(manifest.files[0].sha256, createHash("sha256").update(Buffer.from([0, 1, 2, 255])).digest("hex"));
    assert.equal(JSON.parse(files.get("manifest.json")!.toString()).products.length, 2);
  } finally { await rm(directory, { recursive: true, force: true }); }
});

test("batch ZIP rejects unsafe paths and wrong hashes without publishing a part", async () => {
  const directory = await mkdtemp(join(tmpdir(), "apcs-batch-zip-"));
  try {
    const file = join(directory, "source"); await writeFile(file, "source");
    const product: BatchZipProduct = { id: ids[0]!, name: "shirt", type: "tshirt", assets: [{ id: "a", path: "design/../secret", file }] };
    await assert.rejects(createBatchZipParts(directory, [product], 1024 * 1024, AbortSignal.timeout(5000)), /Unsafe/);
    product.assets[0] = { id: "a", path: "design/a.png", file, sha256: "a".repeat(64) };
    await assert.rejects(createBatchZipParts(directory, [product], 1024 * 1024, AbortSignal.timeout(5000)), /hash changed/);
  } finally { await rm(directory, { recursive: true, force: true }); }
});

test("batch ZIP splits between products and rejects a product larger than a part", async () => {
  const directory = await mkdtemp(join(tmpdir(), "apcs-batch-zip-"));
  try {
    const file = join(directory, "source"); await writeFile(file, Buffer.alloc(90_000, 65));
    const products = ids.map(id => ({ id, name: "shirt", type: "tshirt", assets: [{ id, path: "design/a.png", file }] }));
    const paths = await createBatchZipParts(directory, products, 250_000, AbortSignal.timeout(10_000));
    assert.equal(paths.length, 2);
    for (let index = 0; index < paths.length; index++) {
      const manifest = JSON.parse(entries(await readFile(paths[index]!)).get("manifest.json")!.toString());
      assert.equal(manifest.products.length, 1);
      assert.equal(manifest.products[0].productId, ids[index]);
    }
    await assert.rejects(createBatchZipParts(directory, products, 200_000, AbortSignal.timeout(5000)), /exceeds/);
  } finally { await rm(directory, { recursive: true, force: true }); }
});

test("batch ZIP refuses a part limit above the storage upload maximum", async () => {
  await assert.rejects(createBatchZipParts(tmpdir(), [], 400 * 1024 * 1024 + 1, AbortSignal.timeout(5000)), /Invalid ZIP size limit/);
});

test("batch ZIP writes a product's Etsy listing guide beside its folders and lists it in the manifest", async () => {
  const directory = await mkdtemp(join(tmpdir(), "apcs-batch-zip-"));
  try {
    const mockup = join(directory, "mockup");
    await writeFile(mockup, Buffer.from([7, 8, 9]));
    const guide = "How to list \"Tee\" on Etsy yourself\n1. Photos\n   - mockup/mockup-01.png\n";
    const [path] = await createBatchZipParts(directory,
      [{ id: ids[0]!, name: "Tee", type: "tshirt", assets: [{ id: "m1", path: "mockup/mockup-01.png", file: mockup }], guide }],
      1024 * 1024, AbortSignal.timeout(10_000));
    const files = entries(await readFile(path!));
    const folder = `Tee__${ids[0]}`;
    assert.equal(files.get(`${folder}/ETSY-LISTING-GUIDE.txt`)?.toString(), guide);
    const manifest = JSON.parse(files.get(`${folder}/manifest.json`)!.toString());
    assert.ok(manifest.files.some((file: { path: string; id: string }) => file.path === "ETSY-LISTING-GUIDE.txt" && file.id === "guide"));
    assert.match(files.get("README.txt")!.toString(), /ETSY-LISTING-GUIDE\.txt/);
  } finally { await rm(directory, { recursive: true, force: true }); }
});
