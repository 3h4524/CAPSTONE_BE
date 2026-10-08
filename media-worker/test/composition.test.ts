import assert from "node:assert/strict";
import { test } from "node:test";
import { getSceneFrame, getShotImageGeometry, type StandardVideoScene } from "@apcs/video-composition";

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

test("contain_gentle keeps contain layout while applying a bounded scale", () => {
  const contained = { ...scene, motion: "contain_gentle" };
  assert.equal(getShotImageGeometry(contained, { url: "asset", width: 1200, height: 800 }, 0).mode, "contain");
  assert.equal(getSceneFrame(contained, 0).containScale, .97);
  assert.equal(getSceneFrame(contained, 89).containScale, 1);
});

test("shared fade transition matches first middle and final preview/render opacity", () => {
  const faded = { ...scene, transition: "fade" as const };
  assert.equal(getSceneFrame(faded, 0).opacity, 0);
  assert.equal(getSceneFrame(faded, 45).opacity, 1);
  assert.equal(getSceneFrame(faded, 89).opacity, 0);
});
