using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;

namespace APCS.Infrastructure.Services;

public static partial class RagCacheKeys
{
    public const string GenerationKey = APCS.Application.Features.Rag.Common.RagCacheConstants.SearchGenerationKey;

    public static string NormalizeText(string text) => WhiteSpace().Replace(text.Trim(), " ").ToLowerInvariant();

    public static string Embedding(string model, string text) =>
        $"rag:embedding:{Hash(model, NormalizeText(text))}";

    public static string Search(string generation, RagSearchRequest request) =>
        $"rag:search:{generation}:{Hash(
            NormalizeText(request.Query),
            request.TopK.ToString(),
            request.ContentType?.Trim().ToLowerInvariant(),
            request.Language,
            request.SourceType)}";

    public static bool IsCacheable(RagSearchRequest request)
    {
        var contentType = request.ContentType?.Trim().ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(request.Query)
            && contentType is RagContentTypes.Faq or RagContentTypes.Policy
            && request.Platform is null
            && request.Niche is null
            && request.MinimumViralScore is null;
    }

    private static string Hash(params string?[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts.Select(part => part ?? string.Empty))));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
