import assert from "node:assert/strict";
import { test } from "node:test";
import { uploadRejectionCategory } from "../src/media-files";
import { downloadAssets } from "../src/media-files";
import { failureCode } from "../src/failures";
import type { WorkerJob } from "../src/contracts";

test("upload diagnostics classify provider failures without exposing provider text", () => {
  assert.equal(uploadRejectionCategory("Invalid Signature SECRET: String to sign SECRET"), "invalid upload signature");
  assert.equal(uploadRejectionCategory("File size too large. Got 12. Maximum is 10"), "file exceeds provider size limit");
  assert.equal(uploadRejectionCategory("File format is not allowed"), "file type not allowed");
  assert.equal(uploadRejectionCategory("Unexpected detail SECRET https://example.com/?signature=SECRET"), "unclassified provider rejection");
});

test("source download failures identify the asset and status without leaking the URL", async () => {
  const original = globalThis.fetch;
  globalThis.fetch = async () => new Response(null, { status: 404 });
  try {
    const job = { assets: [{ mockupId: "00000000-0000-0000-0000-000000000000", fileName: "designs/example",
      url: "https://res.cloudinary.com/example/image/upload/file.png?secret=private" }] } as WorkerJob;
    await assert.rejects(downloadAssets(job, ".", AbortSignal.timeout(1000)), error => {
      assert.ok(error instanceof Error);
      assert.equal(error.message, "Could not download designs/example (HTTP 404)");
      assert.equal(failureCode(error, false), "SourceDownloadFailed");
      return true;
    });
  } finally { globalThis.fetch = original; }
});
