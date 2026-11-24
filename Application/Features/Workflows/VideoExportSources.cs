using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>The media each scene of a video is made from, as Export ZIP needs it to make the video of every scene again.</summary>
public static class VideoExportSources
{
    /// <summary>
    /// The sources of <paramref name="board"/>'s scenes: each scene's approved mock-up, and for an AI scene the clip stored for it.
    /// Written when a render completes, and built the same way for a video that was never rendered.
    /// </summary>
    /// <param name="aiPayload">The render job's payload of an AI video, which holds the clips stored for it.</param>
    /// <param name="mockups">The mock-up of every scene, by id.</param>
    public static VideoExportSourceSnapshot Build(Guid videoId, Storyboard board, AiRenderJobPayload? aiPayload, IReadOnlyDictionary<Guid, MockupImage> mockups)
    {
        var scenes = board.Scenes.Select(scene =>
        {
            var source = mockups[scene.MockupId];
            var shot = aiPayload?.Storyboard.AiShots?.SingleOrDefault(item => item.SceneOrder == scene.SceneOrder);
            var clip = shot is null ? null : aiPayload!.AiShotStates.SingleOrDefault(item =>
                item.SceneOrder == shot.SceneOrder && item.Fingerprint == shot.Fingerprint && item.Status == "stored");
            return new VideoSceneExportSource(scene.SceneOrder, scene.MockupId, scene.SourceRevision, scene.SourceHash,
                source.StorageKey, source.StorageVersion, shot?.Fingerprint, clip?.StorageKey, clip?.StorageVersion);
        }).ToArray();
        return new VideoExportSourceSnapshot(1, videoId, board.Fingerprint, scenes);
    }
}
