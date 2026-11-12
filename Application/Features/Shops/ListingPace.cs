using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Shops;

/// <summary>Whether one more listing may go to the seller's Etsy shop now, and if not, from when.</summary>
public sealed record ListingPaceDecision(bool Allowed, DateTime? RetryAtUtc = null, string? Reason = null)
{
    public static readonly ListingPaceDecision Go = new(true);
}

/// <summary>How many listings a shop of a given age gets per hour and per day.</summary>
/// <param name="Stage">The shop's age in the seller's words, for the message.</param>
public sealed record ListingPaceLimit(int PerHour, int PerDay, string Stage);

/// <summary>
/// APCS's own limits, not Etsy's: Etsy publishes none, but reads a young shop that adds many listings at once as a
/// possible fraud or spam account. A shop's pace grows with its age; a shop of unknown age counts as new.
/// </summary>
public static class ListingPaceRules
{
    public static ListingPaceLimit For(DateTimeOffset? openedAt, DateTimeOffset now)
    {
        var age = openedAt is { } opened ? now - opened : TimeSpan.Zero;
        return age < TimeSpan.FromDays(30) ? new ListingPaceLimit(3, 10, "under 30 days old")
            : age < TimeSpan.FromDays(90) ? new ListingPaceLimit(8, 25, "under 90 days old")
            : new ListingPaceLimit(25, 100, "established");
    }
}

/// <summary>Keeps the listings APCS adds to one seller's Etsy shop at a pace a real shop keeps.</summary>
public interface IListingPace
{
    /// <summary>Counts the listings APCS added for the seller in the last day (direct and through Printify).</summary>
    Task<ListingPaceDecision> CheckAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class ListingPace(
    ShopService shops,
    IEtsyShopClient etsy,
    ICacheService cache,
    IRepository<EtsyUploadLog> etsyLogs,
    IRepository<PrintifyUploadLog> printifyLogs,
    TimeProvider time,
    ILogger<ListingPace> logger) : IListingPace
{
    private static readonly TimeSpan OpenedLifetime = TimeSpan.FromDays(1);

    internal static string OpenedKey(Guid userId) => $"etsy:shop-opened:v1:{userId}";

    public async Task<ListingPaceDecision> CheckAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var limit = ListingPaceRules.For(await OpenedAtAsync(userId, cancellationToken), now);
        var since = now.UtcDateTime.AddDays(-1);

        // A Printify product counts from when it was made: it becomes an Etsy listing when published.
        var direct = await etsyLogs.Query()
            .Where(row => row.EtsyIntegration.UserId == userId && row.EtsyListingId != null && row.AttemptedAt >= since)
            .Select(row => row.AttemptedAt)
            .ToListAsync(cancellationToken);
        var throughPrintify = await printifyLogs.Query()
            .Where(row => row.PrintifyIntegration.UserId == userId && row.SyncType == "create" && row.UploadStatus == "success"
                && row.CompletedAt != null && row.CompletedAt >= since)
            .Select(row => row.CompletedAt!.Value)
            .ToListAsync(cancellationToken);
        var added = direct.Concat(throughPrintify).Order().ToList();
        var lastHour = added.Where(at => at >= now.UtcDateTime.AddHours(-1)).ToList();

        DateTime? retryAt = added.Count >= limit.PerDay ? added[^limit.PerDay].AddDays(1)
            : lastHour.Count >= limit.PerHour ? lastHour[^limit.PerHour].AddHours(1)
            : null;
        return retryAt is not { } at
            ? ListingPaceDecision.Go
            : new ListingPaceDecision(false, at,
                $"Etsy watches shops that add many listings at once. While your shop is {limit.Stage}, APCS adds at most {limit.PerHour} listings an hour " +
                $"and {limit.PerDay} a day; the next one can go at {at:yyyy-MM-dd HH:mm} UTC.");
    }

    /// <summary>When the seller's connected shop opened; null when it is not connected or Etsy cannot say.</summary>
    private async Task<DateTimeOffset?> OpenedAtAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await cache.GetAsync<ShopOpened>(OpenedKey(userId), cancellationToken) is { } cached)
            return cached.OpenedAtUtc;
        try
        {
            if (await shops.ConnectedAccessAsync(userId, cancellationToken) is not { } access)
                return null;
            var shop = await etsy.GetShopAsync(access.AccessToken, access.Integration.EtsyShopId, cancellationToken);
            await cache.SetAsync(OpenedKey(userId), new ShopOpened(shop.OpenedAtUtc), OpenedLifetime, cancellationToken);
            return shop.OpenedAtUtc;
        }
        catch (Exception exception) when (exception is EtsyApiException or HttpRequestException)
        {
            logger.LogWarning("The opening date of user {UserId}'s Etsy shop could not be read: {Error}.", userId, exception.GetType().Name);
            return null;
        }
    }

    internal sealed record ShopOpened(DateTimeOffset? OpenedAtUtc);
}
