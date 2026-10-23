import { getSceneFrame, getShotImageGeometry, getTransitionFrameStyle, type StandardVideoScene, type StandardVideoSource, type VideoTransition } from "@apcs/video-composition";
import { AbsoluteFill, Audio, Composition, Img, OffthreadVideo, Sequence, interpolate, registerRoot, useCurrentFrame } from "remotion";
import type { RenderProps } from "./contracts";

function Shot({ scene, source, width, height }: { scene: StandardVideoScene; source: StandardVideoSource; width: number; height: number }) {
  const frame = useCurrentFrame();
  const { opacity, rotation, scale } = getSceneFrame(scene, frame);
  const geometry = getShotImageGeometry(scene, source, frame, { width, height });
  const inset = Math.round(Math.min(width, height) * .074);
  const captionBottom = Math.round(height * .079);
  const captionFontSize = Math.round(Math.min(width, height) * .048);
  const style = geometry.mode === "contain"
    ? { width: "100%", height: "100%", objectFit: "contain" as const, transform: `scale(${geometry.scale * scale}) rotate(${rotation}deg)` }
    : { position: "absolute" as const, width: geometry.width, height: geometry.height, maxWidth: "none", left: geometry.left, top: geometry.top, transform: `scale(${scale}) rotate(${rotation}deg)` };
  return <AbsoluteFill style={{ backgroundColor: "#111827", overflow: "hidden", opacity }}>
    <Img src={source.url} style={style} />
    {scene.text && <div style={{ position: "absolute", bottom: captionBottom, left: inset, right: inset, padding: `${Math.round(captionFontSize * .54)}px ${Math.round(captionFontSize * .69)}px`, backgroundColor: "rgba(0,0,0,.72)", color: "white", fontSize: captionFontSize, lineHeight: 1.35, fontFamily: "Arial, sans-serif", textAlign: "center", overflowWrap: "anywhere" }}>{scene.text}</div>}
  </AbsoluteFill>;
}

function OverlayLayer({ storyboard }: { storyboard: RenderProps["storyboard"] }) {
  const frame = useCurrentFrame(); const overlays = storyboard.videoEdit?.overlays ?? [];
  return <>{overlays.filter(item => frame >= item.startFrame && frame < item.endFrame).map(item => {
    const local = Math.min(frame - item.startFrame, item.endFrame - frame - 1);
    const opacity = Math.min(1, Math.max(0, local / 6));
    return <div key={item.id} style={{ position: "absolute", left: `${item.x * 100}%`, top: `${item.y * 100}%`, transform: "translate(-50%, -50%)", maxWidth: "78%", padding: item.type === "cta" ? "18px 30px" : "10px 16px", color: item.textColor, background: item.preset === "minimal" || item.preset === "outline" ? "transparent" : item.backgroundColor, border: item.preset === "outline" ? `3px solid ${item.textColor}` : undefined, borderRadius: item.preset === "pill" ? 999 : 8, fontFamily: "Arial, sans-serif", fontWeight: item.preset === "bold" || item.type === "cta" ? 700 : 500, fontSize: `${Math.round(item.fontScale * Math.min(storyboard.width, storyboard.height))}px`, lineHeight: 1.1, textAlign: item.align, opacity, overflowWrap: "anywhere" }}>{item.text}</div>;
  })}</>;
}

function Music({ storyboard, src }: { storyboard: RenderProps["storyboard"]; src?: string }) {
  const music = storyboard.videoEdit?.music; if (!music || music.muted || !src) return null;
  return <Sequence from={music.startFrame}><Audio src={src} startFrom={music.sourceOffsetFrames} loop={music.loop} volume={frame => {
    const fadeIn = music.fadeInFrames ? Math.min(1, frame / music.fadeInFrames) : 1;
    const remaining = (storyboard.outputDurationFrames ?? storyboard.durationSeconds * 30) - music.startFrame - frame;
    const fadeOut = music.fadeOutFrames ? Math.min(1, Math.max(0, remaining / music.fadeOutFrames)) : 1;
    return music.volume * Math.min(fadeIn, fadeOut);
  }} /></Sequence>;
}

function Showcase({ storyboard, sources, clips = {}, music }: RenderProps) {
  let offset = 0;
  return <AbsoluteFill style={{ backgroundColor: "#111827" }}>{storyboard.scenes.map((scene, index) => {
    const overlapFrames = scene.transitionFrames || (storyboard.outputDurationFrames == null ? storyboard.transitionOverlapFrames : 0);
    const from = Math.max(0, offset - overlapFrames);
    offset = from + scene.durationFrames;
    const source = sources[scene.mockupId];
    if (!source) throw new Error("Missing immutable scene source");
    const next = storyboard.scenes[index + 1];
    const nextTransition = next?.transition ?? "none";
    const nextOverlapFrames = next?.transitionFrames || (storyboard.outputDurationFrames == null ? storyboard.transitionOverlapFrames : 0);
    return <Sequence key={`${scene.sceneOrder}-${scene.mockupId}`} from={from} durationInFrames={scene.durationFrames}>
      <TransitionLayer transitionIn={scene.transition} transitionOut={nextTransition} overlapFrames={overlapFrames} nextOverlapFrames={nextOverlapFrames}
        sceneIndex={index} durationFrames={scene.durationFrames} generationStrategy={scene.generationStrategy}
        legacy={storyboard.outputDurationFrames == null}>
        {scene.generationStrategy === "ai_shot"
          ? <AiClip src={clips[scene.sceneOrder]} width={storyboard.width} height={storyboard.height} text={scene.text} trimStartFrames={scene.trimStartFrames} />
          : <Shot scene={scene} source={source} width={storyboard.width} height={storyboard.height} />}
      </TransitionLayer>
    </Sequence>;
  })}<OverlayLayer storyboard={storyboard} /><Music storyboard={storyboard} src={music} /></AbsoluteFill>;
}

function TransitionLayer({ children, transitionIn, transitionOut, overlapFrames, nextOverlapFrames, sceneIndex, durationFrames, generationStrategy, legacy }: {
  children: React.ReactNode; transitionIn: VideoTransition; transitionOut: VideoTransition; overlapFrames: number;
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
  } else if (transitionOut !== "none" && transitionOut !== "cut" && nextOverlapFrames > 0 && frame >= durationFrames - nextOverlapFrames) {
    const outgoing = getTransitionFrameStyle(transitionOut, frame - (durationFrames - nextOverlapFrames), nextOverlapFrames, "outgoing");
    style = { ...outgoing, flashOpacity: 0 };
  }
  return <AbsoluteFill style={{ opacity: style.opacity, transform: style.transform, clipPath: style.clipPath as React.CSSProperties["clipPath"], filter: style.filter }}>
    {children}
    {style.flashOpacity > 0 && <AbsoluteFill style={{ backgroundColor: "white", opacity: style.flashOpacity }} />}
  </AbsoluteFill>;
}

function AiClip({ src, width, height, text, trimStartFrames }: { src?: string; width: number; height: number; text: string; trimStartFrames: number }) {
  if (!src) throw new Error("Missing AI Shot clip");
  const inset = Math.round(Math.min(width, height) * .074);
  const captionFontSize = Math.round(Math.min(width, height) * .048);
  return <AbsoluteFill style={{ backgroundColor: "#111827" }}><OffthreadVideo src={src} muted startFrom={trimStartFrames} style={{ width, height, objectFit: "cover" }} />
    {text && <div style={{ position: "absolute", bottom: Math.round(height * .079), left: inset, right: inset, padding: `${Math.round(captionFontSize * .54)}px ${Math.round(captionFontSize * .69)}px`, backgroundColor: "rgba(0,0,0,.72)", color: "white", fontSize: captionFontSize, lineHeight: 1.35, fontFamily: "Arial, sans-serif", textAlign: "center", overflowWrap: "anywhere" }}>{text}</div>}
  </AbsoluteFill>;
}

function Root() {
  return <Composition id="APCS-Standard" component={Showcase} width={1080} height={1920} fps={30} durationInFrames={360}
    defaultProps={{ storyboard: { template: "product_showcase", templateVersion: 1, durationSeconds: 12, productType: "tshirt", fingerprint: "", outputFormat: "portrait", width: 1080, height: 1920, aspectRatio: "9:16", mode: "standard", transitionOverlapFrames: 0, scenes: [] }, sources: {} }}
    calculateMetadata={({ props }) => ({ durationInFrames: props.storyboard.outputDurationFrames ?? props.storyboard.durationSeconds * 30, width: props.storyboard.width, height: props.storyboard.height })} />;
}

registerRoot(Root);
