import type { CSSProperties, ReactNode } from "react";
import { AbsoluteFill, Audio, Freeze, Img, interpolate, OffthreadVideo, Sequence, useCurrentFrame } from "remotion";

import { getSceneFrame, getShotImageGeometry, getTransitionFrameStyle, type StandardVideoScene, type StandardVideoSource, type VideoTransition } from "@apcs/video-composition";

/**
 * The composition of a promo video: photo scenes with their motion, AI clips, transitions, overlays and music.
 *
 * It is the one composition that both draws the final video (the media worker renders it with Remotion) and plays a video in the
 * browser before any file exists (the seller's preview), so what the seller approves is what is rendered.
 *
 * This file is the source. The frontend keeps a copy at `packages/video-composition/src/composition.tsx`, written by
 * `scripts/Sync-VideoComposition.ps1` (which also copies the helpers of `shared/video-composition`); CI fails when the copies differ.
 * It lives here, not in `shared/video-composition`, because it needs the `react` and `remotion` of the app that renders it.
 */

export type CompositionScene = StandardVideoScene & {
  transitionFrames?: number;
  trimStartFrames: number;
  generationStrategy: string;
  aiCropPosition?: { x: number; y: number } | null;
};
export type CompositionOverlay = {
  id: string; type: string; text: string; startFrame: number; endFrame: number; x: number; y: number;
  align: "left" | "center" | "right"; preset: string; fontScale: number; textColor: string; backgroundColor: string;
};
export type CompositionMusic = {
  startFrame: number; sourceOffsetFrames: number; volume: number; fadeInFrames: number; fadeOutFrames: number; loop: boolean; muted: boolean;
};
export type CompositionStoryboard = {
  width: number; height: number; durationSeconds: number;
  outputDurationFrames?: number | null;
  transitionOverlapFrames: number;
  scenes: CompositionScene[];
  videoEdit?: { music?: CompositionMusic | null; overlays?: CompositionOverlay[] | null } | null;
};
/** `clips` and `music` are links the renderer can read: a local address in the media worker, a signed link in the browser. */
export type CompositionProps = {
  storyboard: CompositionStoryboard;
  sources: Record<string, StandardVideoSource>;
  clips?: Record<number, string>;
  music?: string;
};
export type SceneCompositionProps = Omit<CompositionProps, "music"> & { sceneOrder: number };

/** The frame each scene starts at in the final video, including legacy videos that overlap their scenes. */
export function sceneTimelineOffset(storyboard: CompositionStoryboard, sceneOrder: number): number {
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

export const totalFrames = (storyboard: CompositionStoryboard) => storyboard.outputDurationFrames ?? storyboard.durationSeconds * 30;

function Caption({ text, width, height }: { text: string; width: number; height: number }) {
  const inset = Math.round(Math.min(width, height) * .074);
  const captionFontSize = Math.round(Math.min(width, height) * .048);
  return <div style={{ position: "absolute", bottom: Math.round(height * .079), left: inset, right: inset, padding: `${Math.round(captionFontSize * .54)}px ${Math.round(captionFontSize * .69)}px`, backgroundColor: "rgba(0,0,0,.72)", color: "white", fontSize: captionFontSize, lineHeight: 1.35, fontFamily: "Arial, sans-serif", textAlign: "center", overflowWrap: "anywhere" }}>{text}</div>;
}

function Shot({ scene, source, width, height }: { scene: CompositionScene; source: StandardVideoSource; width: number; height: number }) {
  const frame = useCurrentFrame();
  const { opacity, rotation, scale } = getSceneFrame(scene, frame);
  const geometry = getShotImageGeometry(scene, source, frame, { width, height });
  const style: CSSProperties = geometry.mode === "contain"
    ? { width: "100%", height: "100%", objectFit: "contain", transform: `scale(${geometry.scale * scale}) rotate(${rotation}deg)` }
    : { position: "absolute", width: geometry.width, height: geometry.height, maxWidth: "none", left: geometry.left, top: geometry.top, transform: `scale(${scale}) rotate(${rotation}deg)` };
  return <AbsoluteFill style={{ backgroundColor: "#111827", overflow: "hidden", opacity }}>
    <Img src={source.url} style={style} />
    {scene.text && <Caption text={scene.text} width={width} height={height} />}
  </AbsoluteFill>;
}

function AiClip({ src, width, height, text, trimStartFrames, cropPosition }: { src?: string; width: number; height: number; text: string; trimStartFrames: number; cropPosition?: { x: number; y: number } | null }) {
  if (!src) throw new Error("Missing AI Shot clip");
  return <AbsoluteFill style={{ backgroundColor: "#111827" }}><OffthreadVideo src={src} muted startFrom={trimStartFrames} style={{ width, height, objectFit: "cover", objectPosition: `${(cropPosition?.x ?? .5) * 100}% ${(cropPosition?.y ?? .5) * 100}%` }} />
    {text && <Caption text={text} width={width} height={height} />}
  </AbsoluteFill>;
}

function SceneMedia({ scene, storyboard, sources, clips }: { scene: CompositionScene; storyboard: CompositionStoryboard; sources: CompositionProps["sources"]; clips?: CompositionProps["clips"] }) {
  const source = sources[scene.mockupId];
  if (!source) throw new Error("Missing immutable scene source");
  return scene.generationStrategy === "ai_shot"
    ? <AiClip src={clips?.[scene.sceneOrder]} width={storyboard.width} height={storyboard.height} text={scene.text} trimStartFrames={scene.trimStartFrames} cropPosition={scene.aiCropPosition} />
    : <Shot scene={scene} source={source} width={storyboard.width} height={storyboard.height} />;
}

function OverlayLayer({ storyboard, frameOffset = 0 }: { storyboard: CompositionStoryboard; frameOffset?: number }) {
  const frame = useCurrentFrame() + frameOffset; const overlays = storyboard.videoEdit?.overlays ?? [];
  return <>{overlays.filter(item => frame >= item.startFrame && frame < item.endFrame).map(item => {
    const local = Math.min(frame - item.startFrame, item.endFrame - frame - 1);
    const opacity = Math.min(1, Math.max(0, local / 6));
    return <div key={item.id} style={{ position: "absolute", left: `${item.x * 100}%`, top: `${item.y * 100}%`, transform: "translate(-50%, -50%)", maxWidth: "78%", padding: item.type === "cta" ? "18px 30px" : "10px 16px", color: item.textColor, background: item.preset === "minimal" || item.preset === "outline" ? "transparent" : item.backgroundColor, border: item.preset === "outline" ? `3px solid ${item.textColor}` : undefined, borderRadius: item.preset === "pill" ? 999 : 8, fontFamily: "Arial, sans-serif", fontWeight: item.preset === "bold" || item.type === "cta" ? 700 : 500, fontSize: `${Math.round(item.fontScale * Math.min(storyboard.width, storyboard.height))}px`, lineHeight: 1.1, textAlign: item.align, opacity, overflowWrap: "anywhere" }}>{item.text}</div>;
  })}</>;
}

function Music({ storyboard, src }: { storyboard: CompositionStoryboard; src?: string }) {
  const music = storyboard.videoEdit?.music; if (!music || music.muted || !src) return null;
  return <Sequence from={music.startFrame}><Audio src={src} startFrom={music.sourceOffsetFrames} loop={music.loop} volume={frame => {
    const fadeIn = music.fadeInFrames ? Math.min(1, frame / music.fadeInFrames) : 1;
    const remaining = totalFrames(storyboard) - music.startFrame - frame;
    const fadeOut = music.fadeOutFrames ? Math.min(1, Math.max(0, remaining / music.fadeOutFrames)) : 1;
    return music.volume * Math.min(fadeIn, fadeOut);
  }} /></Sequence>;
}

function TransitionLayer({ children, transitionIn, transitionOut, overlapFrames, nextOverlapFrames, sceneIndex, durationFrames, generationStrategy, legacy }: {
  children: ReactNode; transitionIn: VideoTransition; transitionOut: VideoTransition; overlapFrames: number;
  nextOverlapFrames: number;
  sceneIndex: number; durationFrames: number; generationStrategy: string; legacy: boolean;
}) {
  const frame = useCurrentFrame();
  let style = { opacity: 1, transform: "none", clipPath: "none", filter: "none", flashOpacity: 0 };
  if (legacy) {
    if (generationStrategy === "standard" && transitionIn === "fade")
      style = { ...style, opacity: Math.min(frame / 8, Math.max(0, (durationFrames - 1 - frame) / 8)) };
    else if (generationStrategy === "ai_shot" && sceneIndex > 0 && overlapFrames > 0)
      style = { ...style, opacity: interpolate(frame, [0, overlapFrames], [0, 1], { extrapolateRight: "clamp" }) };
  } else if (sceneIndex > 0 && frame < overlapFrames) {
    style = getTransitionFrameStyle(transitionIn, frame, overlapFrames, "incoming");
  } else if (transitionOut !== "none" && transitionOut !== "cut" && nextOverlapFrames > 0 && frame >= durationFrames) {
    const outgoing = getTransitionFrameStyle(transitionOut, frame - durationFrames, nextOverlapFrames, "outgoing");
    style = { ...outgoing, flashOpacity: 0 };
  }
  return <AbsoluteFill style={{ opacity: style.opacity, transform: style.transform, clipPath: style.clipPath as CSSProperties["clipPath"], filter: style.filter }}>
    {!legacy && frame >= durationFrames
      ? <Freeze frame={Math.max(0, durationFrames - 1)}>{children}</Freeze>
      : children}
    {style.flashOpacity > 0 && <AbsoluteFill style={{ backgroundColor: "white", opacity: style.flashOpacity }} />}
  </AbsoluteFill>;
}

/** The whole video: every scene in order, with its transitions, the overlays and the music. */
export function Showcase({ storyboard, sources, clips = {}, music }: CompositionProps) {
  let offset = 0;
  const legacy = storyboard.outputDurationFrames == null;
  return <AbsoluteFill style={{ backgroundColor: "#111827" }}>{storyboard.scenes.map((scene, index) => {
    const overlapFrames = scene.transitionFrames || (storyboard.outputDurationFrames == null ? storyboard.transitionOverlapFrames : 0);
    const from = legacy ? Math.max(0, offset - overlapFrames) : offset;
    offset = legacy ? from + scene.durationFrames : offset + scene.durationFrames;
    const next = storyboard.scenes[index + 1];
    const nextTransition = next?.transition ?? "none";
    const nextOverlapFrames = next?.transitionFrames || (storyboard.outputDurationFrames == null ? storyboard.transitionOverlapFrames : 0);
    const renderDuration = legacy ? scene.durationFrames : scene.durationFrames + nextOverlapFrames;
    return <Sequence key={`${scene.sceneOrder}-${scene.mockupId}`} from={from} durationInFrames={renderDuration}>
      <TransitionLayer transitionIn={scene.transition} transitionOut={nextTransition} overlapFrames={overlapFrames} nextOverlapFrames={nextOverlapFrames}
        sceneIndex={index} durationFrames={scene.durationFrames} generationStrategy={scene.generationStrategy}
        legacy={legacy}>
        <SceneMedia scene={scene} storyboard={storyboard} sources={sources} clips={clips} />
      </TransitionLayer>
    </Sequence>;
  })}<OverlayLayer storyboard={storyboard} /><Music storyboard={storyboard} src={music} /></AbsoluteFill>;
}

/** One scene of the video on its own, as Export ZIP packages it: its picture and its text, without sound (the music belongs to the whole video). */
export function ExportScene({ storyboard, sources, clips = {}, sceneOrder }: SceneCompositionProps) {
  const scene = storyboard.scenes.find(item => item.sceneOrder === sceneOrder);
  if (!scene) throw new Error("Scene is not in the video snapshot");
  const offset = sceneTimelineOffset(storyboard, sceneOrder);
  return <AbsoluteFill style={{ backgroundColor: "#111827", overflow: "hidden" }}>
    <SceneMedia scene={scene} storyboard={storyboard} sources={sources} clips={clips} />
    <OverlayLayer storyboard={storyboard} frameOffset={offset} />
  </AbsoluteFill>;
}
