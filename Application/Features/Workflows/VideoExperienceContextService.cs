using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Retrieves a short, verified and owner-scoped lesson set before an AI scene is compiled.</summary>
public interface IVideoExperienceContextService
{
    Task<IReadOnlyDictionary<int, IReadOnlyList<VideoPromptExperience>>> RetrieveForScenesAsync(Guid ownerId,
        Product product, GenerateVideoConfig config, CancellationToken cancellationToken = default);
}

public sealed class VideoExperienceContextService(IVideoContextRetriever retriever) : IVideoExperienceContextService
{
    private const int MaximumLessons = 3;
    private const int MaximumCharacters = 1500;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<VideoPromptExperience>>> RetrieveForScenesAsync(Guid ownerId,
        Product product, GenerateVideoConfig config, CancellationToken cancellationToken = default)
    {
        if (config.Mode != "ai_shot" || config.AiScenes is not { Count: > 0 })
            return new Dictionary<int, IReadOnlyList<VideoPromptExperience>>();

        var result = new Dictionary<int, IReadOnlyList<VideoPromptExperience>>();
        for (var sceneOrder = 0; sceneOrder < config.AiScenes.Count; sceneOrder++)
        {
            var scene = config.AiScenes[sceneOrder];
            if (scene.Mode == "standard" || string.IsNullOrWhiteSpace(scene.TemplateCode))
                continue;

            var scope = new VideoExperienceSearchRequest(ownerId, scene.TemplateCode, AiVideoModel.ModelKey,
                scene.PrimaryAction);
            var query = $"{product.ProductType} {scene.TemplateCode} {scene.PrimaryAction ?? "product continuity"}";
            try
            {
                var matches = await retriever.SearchAsync(query, scope, MaximumLessons, cancellationToken);
                var remaining = MaximumCharacters;
                var lessons = new List<VideoPromptExperience>(MaximumLessons);
                foreach (var match in matches)
                {
                    var content = NormalizeLesson(match.Content);
                    if (content.Length == 0 || content.Length > remaining)
                        continue;
                    lessons.Add(new(match.DocumentId, match.Version, content));
                    remaining -= content.Length;
                    if (lessons.Count == MaximumLessons || remaining == 0)
                        break;
                }
                if (lessons.Count > 0)
                    result[sceneOrder] = lessons;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Retrieval is optional. The compiler still produces the rule-only prompt snapshot.
            }
        }
        return result;
    }

    private static string NormalizeLesson(string content) => string.Join(' ', content
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
