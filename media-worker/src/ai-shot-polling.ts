/** How the worker waits for an AI scene the provider is still generating. */
export interface AiShotPollingSettings {
  /** Wait before the first status check: the provider almost never finishes a clip within seconds. */
  firstPollMs: number;
  /** Wait between the following status checks. */
  intervalMs: number;
  /** Longest a scene may take, counted from the moment it was first submitted. */
  timeoutMs: number;
}

export interface AiShotProgress { status: string }

export interface AiShotPollingOperations {
  /** Submits the scene, or submits it again after the provider dropped the clip without charging. */
  start(): Promise<AiShotProgress>;
  /** Asks how far the scene is. */
  poll(): Promise<AiShotProgress>;
  delay(ms: number): Promise<void>;
  now(): number;
}

const defaults = { firstPollSeconds: 30, intervalSeconds: 10, timeoutMinutes: 25 } as const;

// A job is stopped after 30 minutes, so a scene must give up before that.
const limits = { firstPollSeconds: [0, 120], intervalSeconds: [5, 60], timeoutMinutes: [5, 28] } as const;

function read(value: string | undefined, fallback: number, [min, max]: readonly [number, number]) {
  if (value === undefined || value.trim() === "") return fallback;
  const parsed = Number(value);
  return Number.isInteger(parsed) ? Math.min(Math.max(parsed, min), max) : fallback;
}

/** Reads AI_SHOT_FIRST_POLL_SECONDS, AI_SHOT_POLL_SECONDS and AI_SHOT_TIMEOUT_MINUTES; a value that is not a whole number falls back to the default and one out of range is held to its nearest limit. */
export function readAiShotPollingSettings(env: Record<string, string | undefined>): AiShotPollingSettings {
  return {
    firstPollMs: read(env.AI_SHOT_FIRST_POLL_SECONDS, defaults.firstPollSeconds, limits.firstPollSeconds) * 1_000,
    intervalMs: read(env.AI_SHOT_POLL_SECONDS, defaults.intervalSeconds, limits.intervalSeconds) * 1_000,
    timeoutMs: read(env.AI_SHOT_TIMEOUT_MINUTES, defaults.timeoutMinutes, limits.timeoutMinutes) * 60_000
  };
}

/**
 * Follows a scene from its first answer until the provider has finished with it. Returns the last state: "ready"
 * when the clip can be fetched, otherwise whatever stopped the wait (a failure, or "polling"/"pending" when the
 * time ran out).
 */
export async function followAiShot(initial: AiShotProgress, operations: AiShotPollingOperations, settings: AiShotPollingSettings): Promise<AiShotProgress> {
  const submittedAt = operations.now();
  let state = initial;
  let firstCheck = true;
  while (state.status === "polling" || state.status === "pending") {
    const remaining = settings.timeoutMs - (operations.now() - submittedAt);
    if (remaining <= 0) break;
    // "pending" again: the provider dropped the clip without charging and the API allows one more submission.
    // It is counted against the same time limit, and the first check waits as long as it did the first time.
    if (state.status === "pending") { state = await operations.start(); firstCheck = true; continue; }
    await operations.delay(Math.min(firstCheck ? settings.firstPollMs : settings.intervalMs, remaining));
    firstCheck = false;
    state = await operations.poll();
  }
  return state;
}
