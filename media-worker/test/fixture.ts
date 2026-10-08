import { createHash } from "node:crypto";
import { deflateSync } from "node:zlib";
import type { Storyboard } from "../src/contracts";

function crc32(buffer: Buffer) {
  let crc = 0xffffffff;
  for (const value of buffer) {
    crc ^= value;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function chunk(type: string, data: Buffer) {
  const header = Buffer.alloc(4); header.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(body));
  return Buffer.concat([header, body, crc]);
}
export function fixtureImage(index = 0) {
  const width = 540, height = 1080;
  const pixels = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const at = y * (width * 3 + 1) + 1 + x * 3;
    const product = x > 100 && x < 440 && y > 180 && y < 870;
    const artwork = x > 180 && x < 360 && y > 390 && y < 650;
    pixels[at] = artwork ? 240 : product ? 50 + index * 25 : 230;
    pixels[at + 1] = artwork ? 120 : product ? 100 : 235;
    pixels[at + 2] = artwork ? 40 : product ? 155 : 240;
  }
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk("IHDR", ihdr), chunk("IDAT", deflateSync(pixels)), chunk("IEND", Buffer.alloc(0))]);
}
export function fixtureBoard(type = "tshirt", image = fixtureImage(), duration = 3): Storyboard {
  const crop = { x: 0, y: 0, width: 1, height: 1 };
  return { template: "product_showcase", templateVersion: 1, outputFormat: "tall", width: 1080, height: 2160, aspectRatio: "1:2", durationSeconds: duration, productType: type, fingerprint: "a".repeat(64), scenes: [0, 1].map(i => ({ mockupId: "fdc00975-356a-4340-857f-a1fa2c273241", sourceRevision: 1, sourceHash: createHash("sha256").update(image).digest("hex"), role: i ? "ArtworkDetail" : "Hero", sceneOrder: i, durationFrames: duration * 15, crop, endCrop: crop, motion: "static", transition: "fade", text: `APCS ${type} fixture`, generationStrategy: "standard", warnings: [] })) };
}
