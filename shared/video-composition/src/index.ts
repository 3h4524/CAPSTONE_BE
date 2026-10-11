export type VideoRegion = { x: number; y: number; width: number; height: number };
export type StandardVideoScene = {
  mockupId: string;
  sceneOrder: number;
  durationFrames: number;
  crop: VideoRegion;
  endCrop: VideoRegion;
  motion: string;
  transition: "fade" | "cut";
  text: string;
};
export type StandardVideoOutput = { width: number; height: number };
export type StandardVideoStoryboard = { durationSeconds: number; width: number; height: number; scenes: StandardVideoScene[] };
export type StandardVideoSource = { url: string; width: number; height: number };
export type ShotImageGeometry =
  | { mode: "contain"; scale: number }
  | { mode: "crop"; width: number; height: number; left: number; top: number };

const clamp = (value: number) => Math.min(1, Math.max(0, value));
const mix = (start: number, end: number, progress: number) => start + (end - start) * progress;

export const getSceneFrame = (scene: StandardVideoScene, frame: number) => {
  const progress = clamp(frame / Math.max(1, scene.durationFrames - 1));
  const crop = Object.fromEntries((["x", "y", "width", "height"] as const).map(key =>
    [key, mix(scene.crop[key], scene.endCrop[key], progress)]
  )) as VideoRegion;
  const fadeIn = clamp(frame / 8);
  const fadeOut = clamp((scene.durationFrames - 1 - frame) / 8);
  const opacity = scene.transition === "fade" ? Math.min(fadeIn, fadeOut) : 1;
  return { crop, opacity, containScale: scene.motion === "contain_gentle" ? .97 + .03 * progress : 1 };
};

export const getShotImageGeometry = (scene: StandardVideoScene, source: StandardVideoSource, frame: number,
  output: StandardVideoOutput = { width: 1080, height: 2160 }): ShotImageGeometry => {
  const { crop, containScale } = getSceneFrame(scene, frame);
  if (scene.motion === "contain_gentle") return { mode: "contain", scale: containScale };
  const scale = Math.min(output.width / (source.width * crop.width), output.height / (source.height * crop.height));
  const width = source.width * scale;
  const height = source.height * scale;
  return {
    mode: "crop", width, height,
    left: (output.width - width * crop.width) / 2 - crop.x * width,
    top: (output.height - height * crop.height) / 2 - crop.y * height,
  };
};
