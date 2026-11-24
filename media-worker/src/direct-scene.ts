import type { SceneRenderProps } from "./contracts";
import { sceneTimelineOffset } from "./scene-timing";

const fps = 30;
const number = (value: number) => String(Math.round(value * 1e6) / 1e6);

/** The encode every scene cut with FFmpeg shares: no sound, h264/yuv420p at 30 fps, BT.709 limited range, as Remotion writes. */
const encode = (frames: number, preset: string, output: string) =>
  ["-an", "-sn", "-dn", "-frames:v", String(frames), "-r", String(fps), "-c:v", "libx264", "-preset", preset, "-crf", "20", "-pix_fmt", "yuv420p",
    "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
    "-movflags", "+faststart", "-map_metadata", "-1", output];

/**
 * FFmpeg arguments that make the file of an AI scene for Export ZIP straight from its clip, as the export composition draws it
 * (ExportScene with AiClip): the clip muted, played from `trimStartFrames`, scaled to cover the frame and cropped at
 * `aiCropPosition`, for the scene's frames. Null when the scene needs the browser: a photo motion scene, a caption, or text the
 * seller added that shows during it. A clip shorter than the scene holds its last frame, as the video element does.
 */
export function directSceneArguments(props: SceneRenderProps, clip: string | undefined, output: string, preset = "medium"): string[] | null {
  const scene = props.storyboard.scenes.find(item => item.sceneOrder === props.sceneOrder);
  if (!scene || scene.generationStrategy !== "ai_shot" || !clip || scene.text) return null;
  const start = sceneTimelineOffset(props.storyboard, props.sceneOrder), end = start + scene.durationFrames;
  if ((props.storyboard.videoEdit?.overlays ?? []).some(item => item.startFrame < end && item.endFrame > start)) return null;
  const { width, height } = props.storyboard;
  const x = Math.min(1, Math.max(0, scene.aiCropPosition?.x ?? .5)), y = Math.min(1, Math.max(0, scene.aiCropPosition?.y ?? .5));
  const filter = [
    // Rounded up, frame n of the video shows the clip's last frame that started by then, as the browser's video element does,
    // whatever the clip's frame rate. The trim is cut after that, on the video's frames: cut before, with -ss, a trim that
    // falls between two frames of the clip drops the frame the browser still shows.
    `fps=${fps}:round=up`,
    `trim=start_frame=${scene.trimStartFrames}`,
    "setpts=PTS-STARTPTS",
    `scale=${width}:${height}:force_original_aspect_ratio=increase:flags=bicubic:out_color_matrix=bt709:out_range=tv`,
    `crop=${width}:${height}:(iw-${width})*${number(x)}:(ih-${height})*${number(y)}`,
    "setsar=1",
    // Longer than any scene, so a clip that ends early still fills it; -frames:v then cuts it to the scene exactly.
    "tpad=stop_mode=clone:stop_duration=60",
    "format=yuv420p",
  ].join(",");
  return ["-y", "-v", "error", "-i", clip, "-vf", filter, ...encode(scene.durationFrames, preset, output)];
}

/**
 * FFmpeg arguments that cut the file of a scene for Export ZIP out of the final video, when the final video shows exactly what
 * the export composition would draw: from the scene's first frame to its last, the final video draws the scene alone (with
 * its caption and the text shown then), unless a transition into the scene overlaps the end of the one before. So the first
 * scene, and a scene with no transition frames, are cut; any other is drawn. The music is left out, as a scene has no sound.
 * Videos planned before the output timeline (outputDurationFrames null) overlapped their scenes differently and are drawn.
 */
export function finalVideoSceneArguments(props: SceneRenderProps, output: string, preset = "medium"): string[] | null {
  const { storyboard, finalVideo } = props;
  const index = storyboard.scenes.findIndex(item => item.sceneOrder === props.sceneOrder);
  const scene = storyboard.scenes[index];
  if (!scene || !finalVideo || storyboard.outputDurationFrames == null || (index > 0 && scene.transitionFrames > 0)) return null;
  const start = sceneTimelineOffset(storyboard, props.sceneOrder);
  // Half a frame before the scene: the seek keeps the frames that start after it, so the first is the scene's own whatever
  // the rounding of the time.
  const seek = start > 0 ? ["-ss", number((start - .5) / fps)] : [];
  return ["-y", "-v", "error", ...seek, "-i", finalVideo, ...encode(scene.durationFrames, preset, output)];
}
