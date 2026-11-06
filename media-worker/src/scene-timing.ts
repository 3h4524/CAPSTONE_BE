import type { Storyboard } from "./contracts";

/** Match the timeline offsets of Showcase, including legacy overlap-based videos. */
export function sceneTimelineOffset(storyboard: Storyboard, sceneOrder: number): number {
  let offset = 0;
  const legacy = storyboard.outputDurationFrames == null;
  for (const scene of storyboard.scenes) {
    const overlap = scene.transitionFrames || (legacy ? storyboard.transitionOverlapFrames : 0);
    const start = legacy ? Math.max(0, offset - overlap) : offset;
    if (scene.sceneOrder === sceneOrder) return start;
    offset = legacy ? start + scene.durationFrames : offset + scene.durationFrames;
  }
  throw new Error("Scene is not in the video snapshot");
}
