import { createHash } from "node:crypto";
import { createWriteStream } from "node:fs";
import { readFile, stat, writeFile } from "node:fs/promises";
import { join } from "node:path";
import archiver from "archiver";
import type { Upload, WorkerJob } from "./contracts";
import { exportPayloadSchema } from "./contracts";

export async function downloadAssets(job: WorkerJob, directory: string, signal: AbortSignal) {
  const downloaded = new Map<string, { file: string; data: Buffer }>();
  for (const [index, asset] of job.assets.entries()) {
    const url = new URL(asset.url);
    if (url.protocol !== "https:" || !["api.cloudinary.com", "res.cloudinary.com"].includes(url.hostname)) throw new Error("Untrusted media host");
    const response = await fetch(url, { signal });
    if (!response.ok || !response.body) throw new Error("Source download failed");
    const chunks: Uint8Array[] = [];
    let length = 0;
    for await (const chunk of response.body) {
      length += chunk.length;
      if (length > 100 * 1024 * 1024) throw new Error("Media download exceeds limit");
      chunks.push(chunk);
    }
    const data = Buffer.concat(chunks);
    const file = join(directory, `asset-${index}`);
    await writeFile(file, data);
    downloaded.set(asset.fileName, { file, data });
  }
  return downloaded;
}
export function pngSource(data: Buffer, hash: string) {
  if (createHash("sha256").update(data).digest("hex") !== hash || !data.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10])) || data.length < 24) throw new Error("Source hash or format mismatch");
  const width = data.readUInt32BE(16), height = data.readUInt32BE(20);
  if (!width || !height || width * height > 32_000_000) throw new Error("Invalid source dimensions");
  return { url: `data:image/png;base64,${data.toString("base64")}`, width, height };
}
export async function uploadFile(grant: Upload, file: string, signal: AbortSignal) {
  const url = new URL(grant.url);
  if (url.protocol !== "https:" || url.hostname !== "api.cloudinary.com") throw new Error("Untrusted upload host");
  if ((await stat(file)).size > 400 * 1024 * 1024) throw new Error("Output exceeds upload limit");
  const form = new FormData();
  for (const [key, value] of Object.entries(grant.fields)) form.append(key, value);
  form.append("file", new Blob([new Uint8Array(await readFile(file))]), grant.resourceType === "raw" ? "package.zip" : grant.resourceType === "video" ? "video.mp4" : "thumbnail.png");
  const response = await fetch(url, { method: "POST", body: form, signal });
  if (!response.ok) throw new Error("Private asset upload failed");
  const result: unknown = await response.json();
  if (!result || typeof result !== "object" || !("version" in result) || typeof result.version !== "number") throw new Error("Invalid upload response");
  return { storageKey: grant.storageKey, storageVersion: String(result.version) };
}
export async function createZip(file: string, assets: Map<string, { file: string; data: Buffer }>, manifest: unknown, signal: AbortSignal) {
  signal.throwIfAborted();
  const safeManifest = exportPayloadSchema.shape.manifest.parse(manifest);
  for (const name of assets.keys()) if (!/^(video\.mp4|thumbnail\.png|mockups\/[a-f0-9-]{36}\.png)$/.test(name)) throw new Error("Unsafe ZIP entry");
  const zip = archiver("zip", { zlib: { level: 6 } });
  const output = createWriteStream(file);
  const abort = () => { zip.abort(); output.destroy(new Error("Export cancelled")); };
  signal.addEventListener("abort", abort, { once: true });
  try {
    await new Promise<void>((resolve, reject) => {
      output.on("close", resolve); output.on("error", reject); zip.on("error", reject); zip.pipe(output);
      for (const [name, asset] of assets) {
        zip.file(asset.file, { name });
      }
      zip.append(JSON.stringify(safeManifest, null, 2), { name: "manifest.json" });
      void zip.finalize().catch(reject);
    });
  } finally { signal.removeEventListener("abort", abort); }
}
