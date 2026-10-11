import { getSceneFrame, getShotImageGeometry, type StandardVideoScene, type StandardVideoSource } from "@apcs/video-composition";
import { AbsoluteFill, Composition, Img, Sequence, registerRoot, useCurrentFrame } from "remotion";
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

function Showcase({ storyboard, sources }: RenderProps) {
  let offset = 0;
  return <AbsoluteFill style={{ backgroundColor: "#111827" }}>{storyboard.scenes.map(scene => {
    const from = offset;
    offset += scene.durationFrames;
    const source = sources[scene.mockupId];
    if (!source) throw new Error("Missing immutable scene source");
    return <Sequence key={`${scene.sceneOrder}-${scene.mockupId}`} from={from} durationInFrames={scene.durationFrames}>
      <Shot scene={scene} source={source} width={storyboard.width} height={storyboard.height} />
    </Sequence>;
  })}</AbsoluteFill>;
}

function Root() {
  return <Composition id="APCS-Standard" component={Showcase} width={1080} height={2160} fps={30} durationInFrames={360}
    defaultProps={{ storyboard: { template: "product_showcase", templateVersion: 1, durationSeconds: 12, productType: "tshirt", fingerprint: "", outputFormat: "tall", width: 1080, height: 2160, aspectRatio: "1:2", scenes: [] }, sources: {} }}
    calculateMetadata={({ props }) => ({ durationInFrames: props.storyboard.durationSeconds * 30, width: props.storyboard.width, height: props.storyboard.height })} />;
}

registerRoot(Root);
