import { ExportScene, Showcase, totalFrames, type CompositionProps, type SceneCompositionProps } from "./video-composition";
import { Composition, registerRoot } from "remotion";
import type { RenderProps, SceneRenderProps } from "./contracts";

// The composition itself lives in the shared package, so the browser plays the video with the very code that renders it.
export { sceneTimelineOffset } from "./video-composition";

function Root() {
  const defaults: RenderProps = { storyboard: { template: "product_showcase", templateVersion: 1, durationSeconds: 12, productType: "tshirt", fingerprint: "", outputFormat: "portrait", width: 1080, height: 1920, aspectRatio: "9:16", mode: "standard", transitionOverlapFrames: 0, scenes: [], durationPolicyVersion: 1 }, sources: {} };
  return <><Composition id="APCS-Standard" component={Showcase as (props: CompositionProps) => React.ReactElement} width={1080} height={1920} fps={30} durationInFrames={360}
    defaultProps={defaults as CompositionProps}
    calculateMetadata={({ props }) => ({ durationInFrames: totalFrames(props.storyboard), width: props.storyboard.width, height: props.storyboard.height })} />
    <Composition id="APCS-Export-Scene" component={ExportScene as (props: SceneCompositionProps) => React.ReactElement} width={1080} height={1920} fps={30} durationInFrames={30}
      defaultProps={{ ...defaults, sceneOrder: 0 } as SceneRenderProps as SceneCompositionProps}
      calculateMetadata={({ props }) => ({ durationInFrames: props.storyboard.scenes.find(scene => scene.sceneOrder === props.sceneOrder)?.durationFrames ?? 30, width: props.storyboard.width, height: props.storyboard.height })} /></>;
}

registerRoot(Root);
