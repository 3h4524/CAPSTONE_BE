import { getSceneFrame, getShotImageGeometry, getTransitionFrameStyle, type StandardVideoScene, type StandardVideoSource, type VideoTransition } from "@apcs/video-composition";
import { AbsoluteFill, Composition, Img, OffthreadVideo, Sequence, interpolate, registerRoot, useCurrentFrame } from "remotion";
import type { RenderProps } from "./contracts";

function Shot({ scene, source, width, height }: { scene: StandardVideoScene; source: StandardVideoSource; width: number; height: number }) {
  const frame = useCurrentFrame();
  const { opacity } = getSceneFrame(scene, frame);
  const geometry = getShotImageGeometry(scene, source, frame, { width, height });
  const inset = Math.round(Math.min(width, height) * .074);
  const captionBottom = Math.round(height * .079);
  const captionFontSize = Math.round(Math.min(width, height) * .048);
  const style = geometry.mode === "contain"
    ? { width: "100%", height: "100%", objectFit: "contain" as const, transform: `scale(${geometry.scale})` }
    : { position: "absolute" as const, width: geometry.width, height: geometry.height, maxWidth: "none", left: geometry.left, top: geometry.top };
  return <AbsoluteFill style={{ backgroundColor: "#111827", overflow: "hidden", opacity }}>
    <Img src={source.url} style={style} />
    {scene.text && <div style={{ position: "absolute", bottom: captionBottom, left: inset, right: inset, padding: `${Math.round(captionFontSize * .54)}px ${Math.round(captionFontSize * .69)}px`, backgroundColor: "rgba(0,0,0,.72)", color: "white", fontSize: captionFontSize, lineHeight: 1.35, fontFamily: "Arial, sans-serif", textAlign: "center", overflowWrap: "anywhere" }}>{scene.text}</div>}
  </AbsoluteFill>;
}

function Showcase({ storyboard, sources, clips = {} }: RenderProps) {
  let offset = 0;
  return <AbsoluteFill style={{ backgroundColor: "#111827" }}>{storyboard.scenes.map((scene, index) => {
    const overlapFrames = storyboard.transitionOverlapFrames;
    const from = Math.max(0, offset - (index > 0 ? overlapFrames : 0));
    offset = from;
    offset += scene.durationFrames;
    const source = sources[scene.mockupId];
    if (!source) throw new Error("Missing immutable scene source");
    const nextTransition = storyboard.scenes[index + 1]?.transition ?? "none";
    return <Sequence key={`${scene.sceneOrder}-${scene.mockupId}`} from={from} durationInFrames={scene.durationFrames}>
      <TransitionLayer transitionIn={scene.transition} transitionOut={nextTransition} overlapFrames={overlapFrames}
        sceneIndex={index} durationFrames={scene.durationFrames} generationStrategy={scene.generationStrategy}
        legacy={storyboard.outputDurationFrames == null}>
        {scene.generationStrategy === "ai_shot"
          ? <AiClip src={clips[scene.sceneOrder]} width={storyboard.width} height={storyboard.height} text={scene.text} />
          : <Shot scene={scene} source={source} width={storyboard.width} height={storyboard.height} />}
      </TransitionLayer>
    </Sequence>;
  })}</AbsoluteFill>;
}

function TransitionLayer({ children, transitionIn, transitionOut, overlapFrames, sceneIndex, durationFrames, generationStrategy, legacy }: {
  children: React.ReactNode; transitionIn: VideoTransition; transitionOut: VideoTransition; overlapFrames: number;
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
  } else if (transitionOut !== "none" && transitionOut !== "cut" && frame >= durationFrames - overlapFrames) {
    const outgoing = getTransitionFrameStyle(transitionOut, frame - (durationFrames - overlapFrames), overlapFrames, "outgoing");
    style = { ...outgoing, flashOpacity: 0 };
  }
  return <AbsoluteFill style={{ opacity: style.opacity, transform: style.transform, clipPath: style.clipPath as React.CSSProperties["clipPath"], filter: style.filter }}>
    {children}
    {style.flashOpacity > 0 && <AbsoluteFill style={{ backgroundColor: "white", opacity: style.flashOpacity }} />}
  </AbsoluteFill>;
}

function AiClip({ src, width, height, text }: { src?: string; width: number; height: number; text: string }) {
  if (!src) throw new Error("Missing AI Shot clip");
  const inset = Math.round(Math.min(width, height) * .074);
  const captionFontSize = Math.round(Math.min(width, height) * .048);
  return <AbsoluteFill style={{ backgroundColor: "#111827" }}><OffthreadVideo src={src} muted style={{ width, height, objectFit: "cover" }} />
    {text && <div style={{ position: "absolute", bottom: Math.round(height * .079), left: inset, right: inset, padding: `${Math.round(captionFontSize * .54)}px ${Math.round(captionFontSize * .69)}px`, backgroundColor: "rgba(0,0,0,.72)", color: "white", fontSize: captionFontSize, lineHeight: 1.35, fontFamily: "Arial, sans-serif", textAlign: "center", overflowWrap: "anywhere" }}>{text}</div>}
  </AbsoluteFill>;
}

function Root() {
  return <Composition id="APCS-Standard" component={Showcase} width={1080} height={1920} fps={30} durationInFrames={360}
    defaultProps={{ storyboard: { template: "product_showcase", templateVersion: 1, durationSeconds: 12, productType: "tshirt", fingerprint: "", outputFormat: "portrait", width: 1080, height: 1920, aspectRatio: "9:16", mode: "standard", transitionOverlapFrames: 0, scenes: [] }, sources: {} }}
    calculateMetadata={({ props }) => ({ durationInFrames: props.storyboard.outputDurationFrames ?? props.storyboard.durationSeconds * 30, width: props.storyboard.width, height: props.storyboard.height })} />;
}

registerRoot(Root);
