import assert from "node:assert/strict";
import { test } from "node:test";
import { validQa } from "../src/qa";
import type { MediaQa } from "../src/contracts";
const baseline: MediaQa = { codec: "h264", pixelFormat: "yuv420p", width: 1080, height: 2160, fps: 30, durationSeconds: 12, bytes: 1_000_000, hasAudio: false, fullDecode: true };
test("technical QA accepts a silent full-decode Etsy preset", () => assert.equal(validQa(baseline, 12), true));
for (const [field, value] of Object.entries({ codec: "hevc", pixelFormat: "yuv444p", width: 720, height: 1080, fps: 29, durationSeconds: 12.2, bytes: 100 * 1024 * 1024, hasAudio: true, fullDecode: false }))
  test(`technical QA rejects invalid ${field}`, () => assert.equal(validQa({ ...baseline, [field]: value }, 12), false));
test("technical QA rejects empty files and nonfinite timings", () => { assert.equal(validQa({ ...baseline, bytes: 0 }, 12), false); assert.equal(validQa({ ...baseline, fps: NaN }, 12), false); });
