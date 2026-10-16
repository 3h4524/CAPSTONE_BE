import assert from "node:assert/strict";
import { test } from "node:test";
import { ApiError, failureCode } from "../src/failures";

test("a failure outside an AI scene is never reported as an AI Shot failure", () => {
  assert.equal(failureCode(new ApiError(400, "validation.failed"), false), "MediaProcessingFailed");
  assert.equal(failureCode(new Error("Missing source mockup"), false), "MediaProcessingFailed");
});

test("an AI scene failure is named by the API's error code, not by its status alone", () => {
  assert.equal(failureCode(new ApiError(409, "VeoSubmissionUncertain"), true), "VeoSubmissionUncertain");
  assert.equal(failureCode(new ApiError(409, "SourceChanged"), true), "SourceChanged");
  // Other conflicts (a lost lease, a clip that is not ready) say nothing about the submission.
  assert.equal(failureCode(new ApiError(409, "LeaseLost"), true), "AiShotFailed");
  assert.equal(failureCode(new ApiError(400, "validation.failed"), true), "AiShotRejected");
  assert.equal(failureCode(new ApiError(500, "VeoBusy"), true), "AiShotFailed");
  assert.equal(failureCode(new Error("AI Shot scene 1 did not finish"), true), "AiShotFailed");
});
