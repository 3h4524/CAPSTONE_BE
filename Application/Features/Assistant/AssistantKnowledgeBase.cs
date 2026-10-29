using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Assistant;

public sealed partial class AssistantKnowledgeBase(
    IRagService rag,
    ICacheService cache,
    ILogger<AssistantKnowledgeBase> logger)
{
    private const int PrefetchTopK = 4;
    private static readonly TimeSpan PrefetchTimeout = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan AnswerTtl = TimeSpan.FromMinutes(30);

    public static bool IsAnswerCacheable(AssistantChatSession session) =>
        session.History.Count == 0 && session.Context?.Canvas is null;

    public static bool ShouldPrefetch(AssistantChatSession session) => session.Context?.Canvas is null;

    public async Task<IReadOnlyList<RagSearchResultDto>> PrefetchAsync(AssistantChatSession session, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PrefetchTimeout);
        try
        {
            return await rag.SearchAsync(new RagSearchRequest(session.Message, PrefetchTopK, ContentType: RagContentTypes.Faq), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Help article prefetch timed out; the model can still call search_faq.");
            return [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Help article prefetch failed with {ExceptionType}", exception.GetType().Name);
            return [];
        }
    }

    public async Task<AssistantCachedAnswer?> GetAnswerAsync(AssistantChatSession session, CancellationToken cancellationToken)
    {
        if (!IsAnswerCacheable(session))
            return null;
        try
        {
            var key = await AnswerKeyAsync(session, cancellationToken);
            return key is null ? null : await cache.GetAsync<AssistantCachedAnswer>(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Answer cache read failed with {ExceptionType}", exception.GetType().Name);
            return null;
        }
    }

    public async Task SaveAnswerAsync(AssistantChatSession session, AssistantCachedAnswer answer, CancellationToken cancellationToken)
    {
        if (!IsAnswerCacheable(session) || string.IsNullOrWhiteSpace(answer.Text))
            return;
        try
        {
            var key = await AnswerKeyAsync(session, cancellationToken);
            if (key is not null)
                await cache.SetAsync(key, answer, AnswerTtl, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Answer cache write failed with {ExceptionType}", exception.GetType().Name);
        }
    }

    private async Task<string?> AnswerKeyAsync(AssistantChatSession session, CancellationToken cancellationToken)
    {
        var generation = await cache.GetAsync<string>(RagCacheConstants.SearchGenerationKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(generation))
            return null;
        var role = session.Caller.IsAdmin && !session.Caller.IsSeller ? "admin" : "seller";
        var normalized = WhiteSpace().Replace(session.Message.Trim(), " ").ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{role}\u001f{normalized}")))[..32].ToLowerInvariant();
        return $"assistant:answer:{generation}:{hash}";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
