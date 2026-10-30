// A refusal by the API, with the error code it names, so a failure is reported as what it is and not guessed from text.
export class ApiError extends Error {
  constructor(readonly status: number, readonly code: string | null) { super(`Internal API rejected request (${status}${code ? ` ${code}` : ""})`); }
}

// What the API is told when a job stops. Only a failure of an AI scene is reported as one: a Standard video has none.
export function failureCode(error: unknown, duringAiShot: boolean): string {
  if (!duringAiShot) return "MediaProcessingFailed";
  if (!(error instanceof ApiError)) return "AiShotFailed";
  if (error.code === "VeoSubmissionUncertain" || error.code === "AiVideoSubmissionUncertain" || error.code === "SourceChanged") return error.code;
  // Over quota or a temporary provider error: nothing was made or billed, so the API waits before trying again.
  if (error.code === "VeoBusy" || error.code === "AiVideoProviderBusy") return "AiVideoBusy";
  return error.status === 400 ? "AiShotRejected" : "AiShotFailed";
}
