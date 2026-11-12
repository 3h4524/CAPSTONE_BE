import { createHash } from "node:crypto";
import { createReadStream, createWriteStream } from "node:fs";
import { rename, rm, stat } from "node:fs/promises";
import { join } from "node:path";
import { Transform } from "node:stream";
import archiver from "archiver";

/** The caller resolves these files from an authorized immutable asset snapshot. */
export type BatchZipAsset = { path: string; file: string; id: string; version?: string | null; sha256?: string | null };
export type BatchZipListing = { title: string; description: string; tags: string[]; version: number };
/** guide: how to list the product on Etsy by hand, written beside its folders as ETSY-LISTING-GUIDE.txt. */
export type BatchZipProduct = { id: string; name: string; type: string; assets: BatchZipAsset[]; listing?: BatchZipListing | null; warnings?: string[]; guide?: string | null };
type CheckedAsset = BatchZipAsset & { size: number; hash: string };
type CheckedProduct = Omit<BatchZipProduct, "assets"> & { folder: string; assets: CheckedAsset[]; listingFiles: Record<string, string> };

const UUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;
const HASH = /^[a-f0-9]{64}$/i;
const GROUPS = ["design", "mockup", "video", "listing"] as const;
const RESERVE = 128 * 1024;
export const GUIDE_FILE = "ETSY-LISTING-GUIDE.txt";
const MAX_GUIDE_BYTES = 64 * 1024;
const MAX_UPLOAD_BYTES = 400 * 1024 * 1024;

function folderFor(product: BatchZipProduct) {
  if (!UUID.test(product.id)) throw new Error("Invalid product identifier");
  const name = product.name.normalize("NFC").replace(/[^\p{L}\p{N}_-]+/gu, "-").replace(/^-+|-+$/g, "").slice(0, 64) || "product";
  return `${name}__${product.id.toLowerCase()}`;
}

function checkedPath(path: string) {
  if (!/^(design|mockup|video)\/[a-zA-Z0-9_.\-/]+$/.test(path) || path.includes("..") || path.includes("//") || path.endsWith("/"))
    throw new Error("Unsafe batch ZIP entry");
  return path;
}

async function hashFile(file: string, signal: AbortSignal) {
  const hash = createHash("sha256");
  const source = createReadStream(file, { signal });
  for await (const chunk of source) hash.update(chunk as Buffer);
  return hash.digest("hex");
}

function listingFiles(listing: BatchZipListing | null | undefined): Record<string, string> {
  if (!listing) return {};
  const value = { title: listing.title, description: listing.description, tags: listing.tags, version: listing.version };
  return { "listing/title.txt": listing.title, "listing/description.txt": listing.description,
    "listing/tags.txt": listing.tags.join("\n"), "listing/listing.json": JSON.stringify(value, null, 2) };
}

/** Creates complete ZIP parts. A product is never split and no partial ZIP is published. */
export async function createBatchZipParts(directory: string, products: BatchZipProduct[], maxPartBytes: number, signal: AbortSignal) {
  if (!Number.isSafeInteger(maxPartBytes) || maxPartBytes <= RESERVE || maxPartBytes > MAX_UPLOAD_BYTES) throw new Error("Invalid ZIP size limit");
  if (!products.length) throw new Error("Select at least one product");
  const seenProducts = new Set<string>();
  const prepared: CheckedProduct[] = [];
  for (const product of products) {
    signal.throwIfAborted();
    const folder = folderFor(product);
    if (seenProducts.has(product.id.toLowerCase())) throw new Error("Duplicate product");
    seenProducts.add(product.id.toLowerCase());
    const seen = new Set<string>();
    const assets: CheckedAsset[] = [];
    for (const asset of product.assets) {
      const path = checkedPath(asset.path);
      if (seen.has(path.toLowerCase())) throw new Error("Duplicate ZIP entry");
      seen.add(path.toLowerCase());
      const info = await stat(asset.file);
      if (!info.isFile()) throw new Error("Export source is not a file");
      const hash = await hashFile(asset.file, signal);
      if (asset.sha256 && (!HASH.test(asset.sha256) || hash !== asset.sha256.toLowerCase())) throw new Error("Export source hash changed");
      assets.push({ ...asset, path, size: info.size, hash });
    }
    const listing = listingFiles(product.listing);
    if (product.guide && Buffer.byteLength(product.guide) > MAX_GUIDE_BYTES) throw new Error("Listing guide is too large");
    if (!assets.length && !Object.keys(listing).length) throw new Error(`Product ${product.id} has no exportable content`);
    prepared.push({ ...product, folder, assets, listingFiles: listing });
  }

  const parts: CheckedProduct[][] = [];
  let current: CheckedProduct[] = [];
  let bytes = RESERVE;
  for (const product of prepared) {
    const contentBytes = product.assets.reduce((sum, asset) => sum + asset.size, 0)
      + Object.values(product.listingFiles).reduce((sum, value) => sum + Buffer.byteLength(value), 0)
      + (product.guide ? Buffer.byteLength(product.guide) : 0)
      + (product.assets.length + Object.keys(product.listingFiles).length + 8) * 2048;
    if (contentBytes + RESERVE > maxPartBytes) throw new Error(`Product ${product.id} exceeds the ZIP part size limit`);
    if (current.length && bytes + contentBytes > maxPartBytes) { parts.push(current); current = []; bytes = RESERVE; }
    current.push(product); bytes += contentBytes;
  }
  if (current.length) parts.push(current);

  const completed: string[] = [];
  try {
    for (const [index, part] of parts.entries()) {
      const target = join(directory, `part-${String(index + 1).padStart(2, "0")}.zip`);
      const temporary = `${target}.tmp`;
      const archive = archiver("zip", { store: true });
      const output = createWriteStream(temporary, { flags: "wx" });
      const abort = () => { archive.abort(); output.destroy(new Error("Export cancelled")); };
      signal.addEventListener("abort", abort, { once: true });
      try {
        await new Promise<void>((resolve, reject) => {
          output.on("close", resolve); output.on("error", reject); archive.on("error", reject); archive.pipe(output);
          const summaries = part.map(product => {
            const manifest = { schemaVersion: 2, productId: product.id, name: product.name, type: product.type,
              files: [...product.assets.map(asset => ({ path: asset.path, id: asset.id, version: asset.version ?? null, sha256: asset.hash })),
                ...Object.keys(product.listingFiles).map(path => ({ path, id: "listing", version: String(product.listing!.version), sha256: createHash("sha256").update(product.listingFiles[path]!).digest("hex") })),
                ...(product.guide ? [{ path: GUIDE_FILE, id: "guide", version: null, sha256: createHash("sha256").update(product.guide).digest("hex") }] : [])],
              missing: GROUPS.filter(group => group === "listing" ? !product.listing : !product.assets.some(asset => asset.path.startsWith(`${group}/`))),
              warnings: product.warnings ?? [] };
            for (const group of GROUPS) archive.append(Buffer.alloc(0), { name: `${product.folder}/${group}/` });
            for (const asset of product.assets) {
              const hash = createHash("sha256");
              const verifier = new Transform({ transform(chunk, _encoding, callback) { hash.update(chunk); callback(null, chunk); },
                flush(callback) { callback(hash.digest("hex") === asset.hash ? undefined : new Error("Export source changed during packaging")); } });
              const input = createReadStream(asset.file, { signal });
              input.on("error", reject); verifier.on("error", reject);
              archive.append(input.pipe(verifier), { name: `${product.folder}/${asset.path}` });
            }
            for (const [path, content] of Object.entries(product.listingFiles)) archive.append(content, { name: `${product.folder}/${path}` });
            if (product.guide) archive.append(product.guide, { name: `${product.folder}/${GUIDE_FILE}` });
            archive.append(JSON.stringify(manifest, null, 2), { name: `${product.folder}/manifest.json` });
            return { productId: product.id, folder: product.folder, missing: manifest.missing };
          });
          archive.append(JSON.stringify({ schemaVersion: 2, part: index + 1, parts: parts.length, products: summaries }, null, 2), { name: "manifest.json" });
          archive.append("Each product contains design, mockup, video and listing folders. See each manifest for included and missing files. To list a product on Etsy yourself, follow the ETSY-LISTING-GUIDE.txt in its folder when it has one.\n", { name: "README.txt" });
          void archive.finalize().catch(reject);
        });
        if ((await stat(temporary)).size > maxPartBytes) throw new Error("ZIP part exceeds the size limit");
        await rename(temporary, target);
        completed.push(target);
      } finally {
        signal.removeEventListener("abort", abort);
        archive.abort(); output.destroy();
        await rm(temporary, { force: true });
      }
    }
    return completed;
  } catch (error) {
    await Promise.all(completed.map(file => rm(file, { force: true })));
    throw error;
  }
}
