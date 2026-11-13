using System.Runtime.CompilerServices;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.Assistant.Common;

namespace APCS.Application.Features.Assistant;

public sealed class AssistantTurnReader(IAssistantTurnBuffer buffer, TimeProvider timeProvider)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async IAsyncEnumerable<AssistantTurnEntry> ReadAsync(
        Guid turnId,
        string? afterId,
        DateTime untilUtc,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var appended = await buffer.WatchAppendsAsync(turnId, cancellationToken);
        var lastId = afterId;
        while (true)
        {
            var entries = await buffer.ReadAfterAsync(turnId, lastId, cancellationToken);
            foreach (var entry in entries)
            {
                yield return entry;
                if (AssistantStreamEvent.IsTerminal(entry.Type))
                    yield break;
                lastId = entry.Id;
            }

            if (entries.Count > 0)
                continue;
            if (timeProvider.GetUtcNow().UtcDateTime >= untilUtc || !await buffer.ExistsAsync(turnId, cancellationToken))
                yield break;
            await appended.WaitAsync(PollInterval, cancellationToken);
        }
    }
}
