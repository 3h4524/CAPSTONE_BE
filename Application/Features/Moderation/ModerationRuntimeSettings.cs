using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Moderation;

/// <summary>
/// Keeps the settings of <c>moderation_settings</c> for the process, read again every
/// <see cref="ModerationOptions.ListRefreshMinutes"/> or at once after an administrator changes them
/// (<see cref="Invalidate"/>). Without the table the defaults apply. Registered as a singleton.
/// </summary>
public sealed class ModerationRuntimeSettings(
    IModerationStore store,
    IOptions<ModerationOptions> options,
    TimeProvider time,
    ILogger<ModerationRuntimeSettings> logger)
{
    private readonly SemaphoreSlim refresh = new(1, 1);
    private ModerationSettingsSnapshot current = ModerationSettingsSnapshot.Default;
    private DateTimeOffset? loadedAt;

    public async Task<ModerationSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (IsFresh()) return current;
        await refresh.WaitAsync(cancellationToken);
        try
        {
            if (IsFresh()) return current;
            try
            {
                current = ModerationSettingsSnapshot.From(await store.LoadSettingsAsync(cancellationToken));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "The moderation settings could not be read; the previous settings stay in use");
            }
            loadedAt = time.GetUtcNow();
            return current;
        }
        finally
        {
            refresh.Release();
        }
    }

    /// <summary>Reads the settings again on next use.</summary>
    public void Invalidate() => loadedAt = null;

    private bool IsFresh() =>
        loadedAt is { } loaded && time.GetUtcNow() - loaded < TimeSpan.FromMinutes(options.Value.ListRefreshMinutes);
}
