using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Moderation;

/// <summary>
/// Keeps one <see cref="BlocklistMatcher"/> for the process and builds it again from the database every
/// <see cref="ModerationOptions.ListRefreshMinutes"/>. When the lists cannot be read, the last matcher is kept.
/// Registered as a singleton.
/// </summary>
public sealed class ModerationBlocklistCache(
    IModerationStore store,
    IOptions<ModerationOptions> options,
    TimeProvider time,
    ILogger<ModerationBlocklistCache> logger)
{
    private readonly SemaphoreSlim refresh = new(1, 1);
    private BlocklistMatcher matcher = BlocklistMatcher.Empty;
    private DateTimeOffset? loadedAt;

    public async Task<BlocklistMatcher> GetAsync(CancellationToken cancellationToken)
    {
        if (IsFresh()) return matcher;
        await refresh.WaitAsync(cancellationToken);
        try
        {
            if (IsFresh()) return matcher;
            try
            {
                var lists = await store.LoadListsAsync(cancellationToken);
                matcher = new BlocklistMatcher(lists.Terms, lists.Allowlist);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "The moderation blocklist could not be read; the previous list stays in use");
            }
            loadedAt = time.GetUtcNow();
            return matcher;
        }
        finally
        {
            refresh.Release();
        }
    }

    /// <summary>Reads the lists again on next use, after an administrator changed them.</summary>
    public void Invalidate() => loadedAt = null;

    private bool IsFresh() =>
        loadedAt is { } loaded && time.GetUtcNow() - loaded < TimeSpan.FromMinutes(options.Value.ListRefreshMinutes);
}
