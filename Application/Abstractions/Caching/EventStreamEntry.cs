using System.Globalization;

namespace APCS.Application.Abstractions.Caching;

public record EventStreamEntry(string Id, string Type, string Data)
{
    public static bool IsEntryId(string value)
    {
        var parts = value.Split('-');
        return parts.Length == 2 && parts.All(part => part.Length > 0 && ulong.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }
}

public interface IEventStreamSignal : IAsyncDisposable
{
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
