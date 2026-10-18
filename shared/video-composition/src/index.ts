export type VideoRegion = { x: number; y: number; width: number; height: number };
export type StandardVideoScene = {
  mockupId: string;
  sceneOrder: number;
  durationFrames: number;
  crop: VideoRegion;
  endCrop: VideoRegion;
  motion: string;
  transition: VideoTransition;
  text: string;
};
export type StandardVideoOutput = { width: number; height: number };
export type StandardVideoStoryboard = { durationSeconds: number; width: number; height: number; scenes: StandardVideoScene[] };
export type StandardVideoSource = { url: string; width: number; height: number };
export type VideoTransition = "none" | "fade" | "swipe" | "zoom" | "flash" | "whip_pan" | "blur" | "cut";
export type TransitionSide = "incoming" | "outgoing";
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
  return { crop, opacity: 1, containScale: scene.motion === "contain_gentle" ? .97 + .03 * progress : 1 };
};

export const getTransitionFrameStyle = (transition: VideoTransition, frame: number, overlapFrames: number, side: TransitionSide) => {
  if (overlapFrames <= 0 || transition === "none" || transition === "cut") return { opacity: 1, transform: "none", clipPath: "none", filter: "none", flashOpacity: 0 };
  const progress = clamp(frame / Math.max(1, overlapFrames - 1));
  if (transition === "fade") return { opacity: side === "incoming" ? progress : 1, transform: "none", clipPath: "none", filter: "none", flashOpacity: 0 };
  if (transition === "swipe") return { opacity: 1, transform: "none", clipPath: side === "incoming" ? `inset(0 ${(1 - progress) * 100}% 0 0)` : "none", filter: "none", flashOpacity: 0 };
  if (transition === "zoom") return { opacity: side === "incoming" ? .35 + .65 * progress : 1, transform: `scale(${side === "incoming" ? .9 + .1 * progress : 1 + .08 * progress})`, clipPath: "none", filter: "none", flashOpacity: 0 };
  if (transition === "flash") return { opacity: 1, transform: "none", clipPath: "none", filter: "none", flashOpacity: Math.sin(progress * Math.PI) * .82 };
  if (transition === "whip_pan") return { opacity: 1, transform: `translateX(${side === "incoming" ? (1 - progress) * 100 : -progress * 12}%)`, clipPath: "none", filter: `blur(${(side === "incoming" ? 1 - progress : progress) * 7}px)`, flashOpacity: 0 };
  return { opacity: side === "incoming" ? progress : 1, transform: "none", clipPath: "none", filter: side === "incoming" ? `blur(${(1 - progress) * 14}px)` : "none", flashOpacity: 0 };
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
