import assert from "node:assert/strict";
import { test } from "node:test";
import { fixtureBoard } from "./fixture";
import { sceneTimelineOffset } from "../src/scene-timing";

test("scene export uses the exact modern and legacy timeline start", () => {
  const board = fixtureBoard();
  const scenes = board.scenes.map((scene, index) => ({ ...scene, sceneOrder: index, durationFrames: 60, transitionFrames: index ? 12 : 0 }));
  assert.equal(sceneTimelineOffset({ ...board, outputDurationFrames: 120, scenes }, 1), 60);
  assert.equal(sceneTimelineOffset({ ...board, outputDurationFrames: null, transitionOverlapFrames: 12, scenes }, 1), 48);
  assert.throws(() => sceneTimelineOffset({ ...board, scenes }, 3), /not in the video/);
});
