import assert from "node:assert/strict";
import { test } from "node:test";
import { uploadRejectionCategory } from "../src/media-files";

test("upload diagnostics classify provider failures without exposing provider text", () => {
  assert.equal(uploadRejectionCategory("Invalid Signature SECRET: String to sign SECRET"), "invalid upload signature");
  assert.equal(uploadRejectionCategory("File size too large. Got 12. Maximum is 10"), "file exceeds provider size limit");
  assert.equal(uploadRejectionCategory("File format is not allowed"), "file type not allowed");
  assert.equal(uploadRejectionCategory("Unexpected detail SECRET https://example.com/?signature=SECRET"), "unclassified provider rejection");
});
