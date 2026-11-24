import assert from "node:assert/strict";
import { test } from "node:test";
import { directSceneArguments } from "../src/direct-scene";
import { fixtureBoard, fixtureImage } from "./fixture";

const base = fixtureBoard("tshirt", fixtureImage(), 6);
const ai = { ...base.scenes[0]!, generationStrategy: "ai_shot" as const, text: "", durationFrames: 90, trimStartFrames: 15 };
const board = { ...base, mode: "ai_shot" as const, width: 1080, height: 1920, outputDurationFrames: 180,
  scenes: [{ ...ai, sceneOrder: 0 }, { ...base.scenes[1]!, sceneOrder: 1, durationFrames: 90, text: "" }] };
const props = (storyboard = board, sceneOrder = 0) => ({ storyboard, sceneOrder, sources: {}, clips: { 0: "clip.mp4" } });
const overlay = (startFrame: number, endFrame: number) => ({ id: "o", type: "headline" as const, text: "Sale", startFrame, endFrame, x: .5, y: .5,
  align: "center" as const, preset: "clean" as const, fontScale: .05, textColor: "#FFFFFF", backgroundColor: "#000000" });

test("an AI scene with no text is cut from its clip: from its trim, muted, covering the frame, for its frames exactly", () => {
  const args = directSceneArguments(props(), "clip.mp4", "out.mp4")!;
  assert.ok(args);
  assert.match(args[args.indexOf("-vf") + 1]!, /^fps=30:round=up,trim=start_frame=15,setpts=PTS-STARTPTS,/, "fifteen frames in, counted on the video's frames");
  assert.equal(args[args.indexOf("-frames:v") + 1], "90");
  assert.ok(args.includes("-an"), "no sound");
  const filter = args[args.indexOf("-vf") + 1]!;
  assert.match(filter, /scale=1080:1920:force_original_aspect_ratio=increase/);
  assert.match(filter, /crop=1080:1920:\(iw-1080\)\*0\.5:\(ih-1920\)\*0\.5/, "centred when no crop position is set");
  assert.equal(args.at(-1), "out.mp4");
});

test("the crop follows the scene's crop position", () => {
  const storyboard = { ...board, scenes: [{ ...board.scenes[0]!, aiCropPosition: { x: 0, y: .25 } }, board.scenes[1]!] };
  const args = directSceneArguments(props(storyboard), "clip.mp4", "out.mp4")!;
  assert.match(args[args.indexOf("-vf") + 1]!, /crop=1080:1920:\(iw-1080\)\*0:\(ih-1920\)\*0\.25/);
});

test("a scene that needs the browser is not cut: photo motion, a caption, text shown during it, or no clip", () => {
  assert.equal(directSceneArguments(props(board, 1), "clip.mp4", "out.mp4"), null, "photo motion");
  assert.equal(directSceneArguments(props(), undefined, "out.mp4"), null, "no clip");
  const captioned = { ...board, scenes: [{ ...board.scenes[0]!, text: "Soft cotton" }, board.scenes[1]!] };
  assert.equal(directSceneArguments(props(captioned), "clip.mp4", "out.mp4"), null, "caption");
  // Scene 1 is frames 0-89 of the video: text over frames 80-120 shows during it, text from frame 90 on does not.
  const during = { ...board, videoEdit: { version: 1 as const, music: null, overlays: [overlay(80, 120)] } };
  assert.equal(directSceneArguments(props(during), "clip.mp4", "out.mp4"), null, "text during the scene");
  const after = { ...board, videoEdit: { version: 1 as const, music: null, overlays: [overlay(90, 150)] } };
  assert.ok(directSceneArguments(props(after), "clip.mp4", "out.mp4"), "text after the scene");
});

test("a scene with no transition into it is cut from the final video, from its first frame to its last", async () => {
  const { finalVideoSceneArguments } = await import("../src/direct-scene");
  // Scene 1 is frames 0-89, scene 2 frames 90-179 of the video.
  const first = finalVideoSceneArguments({ ...props(), finalVideo: "final.mp4" }, "out.mp4")!;
  assert.ok(!first.includes("-ss"), "the first scene starts with the video");
  assert.equal(first[first.indexOf("-frames:v") + 1], "90");
  assert.ok(first.includes("-an"), "no sound, though the final video has music");
  const second = finalVideoSceneArguments({ ...props(board, 1), finalVideo: "final.mp4" }, "out.mp4")!;
  assert.equal(second[second.indexOf("-ss") + 1], "2.983333", "half a frame before frame 90 (89.5 / 30)");
  assert.equal(second[second.indexOf("-i") + 1], "final.mp4");
});

test("a scene is not cut from the final video when a transition into it overlaps the scene before, or there is no final video", async () => {
  const { finalVideoSceneArguments } = await import("../src/direct-scene");
  assert.equal(finalVideoSceneArguments(props(board, 1), "out.mp4"), null, "no final video");
  const faded = { ...board, scenes: [board.scenes[0]!, { ...board.scenes[1]!, transition: "fade" as const, transitionFrames: 12 }] };
  assert.equal(finalVideoSceneArguments({ ...props(faded, 1), finalVideo: "final.mp4" }, "out.mp4"), null, "fade into scene 2");
  assert.ok(finalVideoSceneArguments({ ...props(faded, 0), finalVideo: "final.mp4" }, "out.mp4"), "scene 1 has nothing before it");
  const legacy = { ...board, outputDurationFrames: null };
  assert.equal(finalVideoSceneArguments({ ...props(legacy as unknown as typeof board, 0), finalVideo: "final.mp4" }, "out.mp4"), null, "planned before the output timeline");
});
