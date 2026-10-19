import assert from "node:assert/strict";
import { test } from "node:test";
import { getSceneFrame, getShotImageGeometry, getTransitionFrameStyle, type StandardVideoScene, type VideoTransition } from "@apcs/video-composition";

const scene: StandardVideoScene = {
  mockupId: "asset", sceneOrder: 0, durationFrames: 90,
  crop: { x: 0, y: 0, width: 1, height: 1 }, endCrop: { x: .1, y: .1, width: .8, height: .8 },
  motion: "zoom", transition: "cut", text: "",
};

test("shared composition interpolates the same crop at first middle and final frames", () => {
  assert.deepEqual(getSceneFrame(scene, 0).crop, scene.crop);
  assert.deepEqual(getSceneFrame(scene, 89).crop, scene.endCrop);
  assert.equal(getSceneFrame(scene, 44.5).crop.width, .9);
});

test("custom first and last keyframes interpolate framing on one source image", () => {
  const startCrop = { x: .08, y: .12, width: .82, height: .76 };
  const endCrop = { x: .2, y: .15, width: .62, height: .68 };
  const custom = { ...scene, mockupId: "approved-keyframe", crop: startCrop, endCrop };

  assert.equal(custom.mockupId, "approved-keyframe");
  assert.deepEqual(getSceneFrame(custom, 0).crop, startCrop);
  assert.deepEqual(getSceneFrame(custom, 89).crop, endCrop);
  assert.deepEqual(getSceneFrame(custom, 44.5).crop, {
    x: .14, y: .135, width: .72, height: .72,
  });
  const firstGeometry = getShotImageGeometry(custom, { url: "same-image", width: 1200, height: 1600 }, 0);
  const lastGeometry = getShotImageGeometry(custom, { url: "same-image", width: 1200, height: 1600 }, 89);
  assert.equal(firstGeometry.mode, "crop");
  assert.equal(lastGeometry.mode, "crop");
  assert.notDeepEqual(firstGeometry, lastGeometry);
});

test("contain_gentle keeps contain layout while applying a bounded scale", () => {
  const contained = { ...scene, motion: "contain_gentle" };
  assert.equal(getShotImageGeometry(contained, { url: "asset", width: 1200, height: 800 }, 0).mode, "contain");
  assert.equal(getSceneFrame(contained, 0).containScale, .97);
  assert.equal(getSceneFrame(contained, 89).containScale, 1);
});

test("scene imagery stays fully visible and transitions are applied only at scene boundaries", () => {
  assert.equal(getSceneFrame({ ...scene, transition: "fade" }, 0).opacity, 1);
  assert.equal(getTransitionFrameStyle("fade", 0, 12, "incoming").opacity, 0);
  assert.equal(getTransitionFrameStyle("fade", 11, 12, "incoming").opacity, 1);
  assert.equal(getTransitionFrameStyle("fade", 0, 12, "outgoing").opacity, 1);
  assert.equal(getTransitionFrameStyle("fade", 11, 12, "outgoing").opacity, 1);
});

test("transition frame styles cover every effect and None/cut add no effect", () => {
  const transitions: VideoTransition[] = ["none", "cut", "fade", "swipe", "zoom", "flash", "whip_pan", "blur"];
  for (const transition of transitions) {
    const start = getTransitionFrameStyle(transition, 0, 12, "incoming");
    const middle = getTransitionFrameStyle(transition, 6, 12, "incoming");
    const end = getTransitionFrameStyle(transition, 11, 12, "incoming");
    if (transition === "none" || transition === "cut") {
      assert.deepEqual(start, { opacity: 1, transform: "none", clipPath: "none", filter: "none", flashOpacity: 0 });
      assert.deepEqual(middle, start);
      assert.deepEqual(end, start);
    } else {
      assert.notDeepEqual(start, middle, `${transition} changes through the overlap`);
      assert.notDeepEqual(middle, end, `${transition} reaches its finished state`);
      assert.equal(getTransitionFrameStyle(transition, 6, 12, "outgoing").opacity >= 0, true);
    }
  }
  assert.match(getTransitionFrameStyle("swipe", 0, 12, "incoming").clipPath, /100%/);
  assert.ok(getTransitionFrameStyle("flash", 6, 12, "incoming").flashOpacity > .8);
  assert.equal(getTransitionFrameStyle("whip_pan", 0, 12, "incoming").transform, "translateX(100%)");
  assert.equal(getTransitionFrameStyle("blur", 0, 12, "incoming").filter, "blur(14px)");
});
