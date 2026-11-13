using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Shops.Common;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Shops;

/// <summary>
/// Connects a seller's Etsy shop with OAuth 2.0 and PKCE, and reads it back.
/// </summary>
/// <remarks>
/// <para>
/// <c>oauth_expires_at</c> holds when the seller must connect again (the refresh token's end), not the
/// access token's: the access token lives about an hour and is renewed silently, and the dashboard reads
/// <c>oauth_expires_at</c> as "this connection has expired". The access token's own expiry is stored with
/// it, inside the encrypted value, as <c>{unix seconds}:{token}</c>.
/// </para>
/// <para>
/// Etsy rotates the refresh token on every refresh, so each refresh stores the new pair and moves
/// <c>oauth_expires_at</c> forward.
/// </para>
/// </remarks>
public sealed class ShopService(
    ICurrentUser currentUser,
    ICacheService cache,
    IEtsyShopClient etsy,
    IRepository<EtsyIntegration> integrations,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ShopService> logger) : IShopService
{
    /// <summary>How long a seller has to finish Etsy's consent page.</summary>
    internal static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Etsy refresh tokens stop working this long after they are issued.</summary>
    internal static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);

    /// <summary>An access token this close to expiry is renewed before use.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    internal static string StateKey(string state) => $"etsy:oauth:state:{state}";

    public async Task<Result<StartEtsyConnectionResponseDto>> StartEtsyConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<StartEtsyConnectionResponseDto>(ShopErrors.Unauthenticated());
        if (!etsy.IsConfigured)
            return Result.Failure<StartEtsyConnectionResponseDto>(ShopErrors.EtsyNotConfigured());

        var state = RandomToken(32);
        var verifier = RandomToken(64);
        await cache.SetAsync(StateKey(state), new PendingEtsyConnection(userId, verifier), StateLifetime, cancellationToken);
        return Result.Success(new StartEtsyConnectionResponseDto(etsy.BuildAuthorizationUrl(state, CodeChallenge(verifier))));
    }

    public async Task<CompleteEtsyConnectionResponseDto> CompleteEtsyConnectionAsync(string? code, string? state, string? error, CancellationToken cancellationToken = default)
    {
        PendingEtsyConnection? pending = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            pending = await cache.GetAsync<PendingEtsyConnection>(StateKey(state), cancellationToken);
            // One use only: a replayed callback finds nothing.
            await cache.RemoveAsync(StateKey(state), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(error))
            return Return(pending is null ? EtsyConnectionOutcomes.Expired : EtsyConnectionOutcomes.Denied);
        if (pending is null || string.IsNullOrWhiteSpace(code))
            return Return(EtsyConnectionOutcomes.Expired);

        try
        {
            var tokens = await etsy.ExchangeCodeAsync(code, pending.CodeVerifier, cancellationToken);
            var shopId = await etsy.GetOwnShopIdAsync(tokens.AccessToken, cancellationToken);
            if (shopId is null)
                return Return(EtsyConnectionOutcomes.NoShop);

            // One Etsy shop answers to one APCS account: Etsy reads a new shop run from several accounts as possible fraud.
            var takenBy = await integrations.FindAsync(row => row.EtsyShopId == shopId && row.UserId != pending.UserId
                && row.IsActive == true && row.DeletedAt == null, cancellationToken);
            if (takenBy.Count > 0)
            {
                logger.LogWarning("User {UserId} tried to connect Etsy shop {ShopId}, already connected to another account.", pending.UserId, shopId);
                return Return(EtsyConnectionOutcomes.ShopTaken);
            }

            var shop = await etsy.GetShopAsync(tokens.AccessToken, shopId, cancellationToken);
            await SaveConnectionAsync(pending.UserId, shop, tokens, cancellationToken);
            return Return(EtsyConnectionOutcomes.Connected);
        }
        catch (EtsyApiException exception)
        {
            logger.LogWarning("Etsy connection for user {UserId} failed: {Kind}.", pending.UserId, exception.Kind);
            return Return(EtsyConnectionOutcomes.Failed);
        }
    }

    public async Task<Result<GetMyShopResponseDto>> GetMyShopAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<GetMyShopResponseDto>(ShopErrors.Unauthenticated());

        var integration = await FindActiveAsync(userId, cancellationToken);
        if (integration is null)
            return Result.Success(new GetMyShopResponseDto(ShopConnectionStatuses.NotConnected, etsy.IsConfigured, null, null, null, null));

        var now = timeProvider.GetUtcNow();
        var stored = StoredShop(integration);
        if (integration.OauthExpiresAt is { } reconnectBy && reconnectBy <= now.UtcDateTime)
            return Result.Success(Describe(ShopConnectionStatuses.ReconnectRequired, integration, stored));

        try
        {
            var accessToken = await AccessTokenAsync(integration, now, cancellationToken);
            var shop = await etsy.GetShopAsync(accessToken, integration.EtsyShopId, cancellationToken);
            integration.ShopName = Truncate(shop.ShopName);
            integration.LastSyncAt = now.UtcDateTime;
            integration.UpdatedAt = now.UtcDateTime;
            await integrations.UpdateAsync(integration, saveChange: true, cancellationToken);
            return Result.Success(Describe(ShopConnectionStatuses.Connected, integration, Map(shop)));
        }
        catch (EtsyApiException exception) when (exception.Kind == EtsyFailureKind.Unauthorized)
        {
            // The grant was revoked on Etsy or the refresh token expired: mark it so the dashboard asks to reconnect.
            integration.OauthExpiresAt = now.UtcDateTime;
            integration.UpdatedAt = now.UtcDateTime;
            await integrations.UpdateAsync(integration, saveChange: true, cancellationToken);
            return Result.Success(Describe(ShopConnectionStatuses.ReconnectRequired, integration, stored));
        }
        catch (EtsyApiException exception)
        {
            logger.LogWarning("Reading the Etsy shop of user {UserId} failed: {Kind}.", userId, exception.Kind);
            return Result.Success(Describe(ShopConnectionStatuses.Unavailable, integration, stored));
        }
    }

    /// <summary>
    /// For other shop use cases (publishing): the seller's active connection with a working access token, renewed
    /// first when due. Null when nothing is connected or the connection must be renewed by the seller.
    /// Throws <see cref="EtsyApiException"/> when renewing fails; see <see cref="MarkReconnectRequiredAsync"/>.
    /// </summary>
    internal async Task<(EtsyIntegration Integration, string AccessToken)?> ConnectedAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        var integration = await FindActiveAsync(userId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (integration is null || (integration.OauthExpiresAt is { } reconnectBy && reconnectBy <= now.UtcDateTime))
            return null;
        return (integration, await AccessTokenAsync(integration, now, cancellationToken));
    }

    /// <summary>Records that Etsy no longer honours the grant, so the shop page and dashboard ask to reconnect.</summary>
    internal async Task MarkReconnectRequiredAsync(EtsyIntegration integration, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        integration.OauthExpiresAt = now;
        integration.UpdatedAt = now;
        await integrations.UpdateAsync(integration, saveChange: true, cancellationToken);
    }

    /// <summary>How long computed insights are reused before Etsy is read again.</summary>
    internal static readonly TimeSpan InsightsLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Orders read per refresh; a busier shop is reported as truncated.</summary>
    internal const int MaxReceipts = 500;

    /// <summary>Payments read per refresh (one call each), newest paid orders first.</summary>
    internal const int MaxPaymentLookups = 25;

    private static readonly string[] CountedListingStates = ["draft", "inactive", "sold_out", "expired"];

    internal static string InsightsKey(Guid userId) => $"shop:insights:{userId}";

    public async Task<Result<GetShopInsightsResponseDto>> GetShopInsightsAsync(bool refresh, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<GetShopInsightsResponseDto>(ShopErrors.Unauthenticated());

        var integration = await FindActiveAsync(userId, cancellationToken);
        if (integration is null)
            return Result.Failure<GetShopInsightsResponseDto>(ShopErrors.NotConnected());

        var now = timeProvider.GetUtcNow();
        if (integration.OauthExpiresAt is { } reconnectBy && reconnectBy <= now.UtcDateTime)
            return Result.Success(EmptyInsights(ShopConnectionStatuses.ReconnectRequired, now));

        if (!refresh && await cache.GetAsync<GetShopInsightsResponseDto>(InsightsKey(userId), cancellationToken) is { } cached)
            return Result.Success(cached);

        try
        {
            var token = await AccessTokenAsync(integration, now, cancellationToken);
            var shop = await etsy.GetShopAsync(token, integration.EtsyShopId, cancellationToken);
            var currency = shop.CurrencyCode ?? "USD";
            var unavailable = new List<string>();

            // Each section is read on its own: a rate limit on one leaves the others on the page.
            var setup = await SectionAsync("setup", unavailable, () => etsy.GetShopSetupAsync(token, shop.ShopId, cancellationToken));
            var listings = await SectionAsync("listings", unavailable, async () =>
            {
                var counts = new Dictionary<string, int> { ["active"] = shop.ActiveListingCount };
                foreach (var state in CountedListingStates)
                    counts[state] = (await etsy.GetListingsAsync(token, shop.ShopId, state, 1, false, cancellationToken)).Count;
                var recent = await etsy.GetListingsAsync(token, shop.ShopId, "active", 50, true, cancellationToken);
                return ShopInsightsCalculator.Listings(counts, recent.Listings, now);
            });
            var orders = await SectionAsync("orders", unavailable, async () =>
            {
                var receipts = await etsy.GetReceiptsAsync(token, shop.ShopId,
                    now.AddDays(-2 * ShopInsightsCalculator.WindowDays), now, MaxReceipts, cancellationToken);
                var payments = await PaymentsAsync(token, shop.ShopId, receipts, now, cancellationToken);
                return ShopInsightsCalculator.Orders(receipts, payments, currency, now, receipts.Count >= MaxReceipts);
            });
            var reviews = await SectionAsync("reviews", unavailable, async () =>
                ShopInsightsCalculator.Reviews(await etsy.GetReviewsAsync(token, shop.ShopId, 100, cancellationToken)));

            integration.ShopName = Truncate(shop.ShopName);
            integration.LastSyncAt = now.UtcDateTime;
            integration.UpdatedAt = now.UtcDateTime;
            await integrations.UpdateAsync(integration, saveChange: true, cancellationToken);

            var insights = new GetShopInsightsResponseDto(ShopConnectionStatuses.Connected, now, currency,
                ShopInsightsCalculator.Health(shop, setup), listings, orders, reviews, unavailable);
            await cache.SetAsync(InsightsKey(userId), insights, InsightsLifetime, cancellationToken);
            return Result.Success(insights);
        }
        catch (EtsyApiException exception) when (exception.Kind == EtsyFailureKind.Unauthorized)
        {
            integration.OauthExpiresAt = now.UtcDateTime;
            integration.UpdatedAt = now.UtcDateTime;
            await integrations.UpdateAsync(integration, saveChange: true, cancellationToken);
            return Result.Success(EmptyInsights(ShopConnectionStatuses.ReconnectRequired, now));
        }
        catch (EtsyApiException exception)
        {
            logger.LogWarning("Reading Etsy insights of user {UserId} failed: {Kind}.", userId, exception.Kind);
            return Result.Success(EmptyInsights(ShopConnectionStatuses.Unavailable, now));
        }
    }

    /// <summary>
    /// Runs one section. A lost grant still ends the whole read (the seller must reconnect); any other Etsy
    /// failure only blanks this section.
    /// </summary>
    private async Task<T?> SectionAsync<T>(string name, List<string> unavailable, Func<Task<T>> read) where T : class
    {
        try
        {
            return await read();
        }
        catch (EtsyApiException exception) when (exception.Kind != EtsyFailureKind.Unauthorized)
        {
            logger.LogWarning("Etsy shop section {Section} unavailable: {Kind}.", name, exception.Kind);
            unavailable.Add(name);
            return null;
        }
    }

    /// <summary>Payments of the newest paid orders in the current window, one call each, skipping any that fail.</summary>
    private async Task<IReadOnlyDictionary<string, EtsyPayment>> PaymentsAsync(
        string token, string shopId, IReadOnlyList<EtsyReceipt> receipts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var payments = new Dictionary<string, EtsyPayment>();
        var windowStart = now.AddDays(-ShopInsightsCalculator.WindowDays);
        foreach (var receipt in receipts.Where(item => item.IsPaid && item.CreatedAtUtc > windowStart).Take(MaxPaymentLookups))
        {
            try
            {
                if (await etsy.GetReceiptPaymentAsync(token, shopId, receipt.ReceiptId, cancellationToken) is { } payment)
                    payments[receipt.ReceiptId] = payment;
            }
            catch (EtsyApiException exception) when (exception.Kind != EtsyFailureKind.Unauthorized)
            {
                // One missing payment lowers the sample, which the response reports; it does not drop the section.
            }
        }
        return payments;
    }

    private static GetShopInsightsResponseDto EmptyInsights(string status, DateTimeOffset now) =>
        new(status, now, null, [], null, null, null, []);

    public async Task<Result> DisconnectEtsyAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure(ShopErrors.Unauthenticated());

        var rows = await integrations.FindAsync(
            row => row.UserId == userId && row.DeletedAt == null, cancellationToken);
        if (rows.Count == 0)
            return Result.Failure(ShopErrors.NotConnected());

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var row in rows)
        {
            // The tokens are blanked, not kept: a disconnected shop must not be usable from a stored token.
            row.IsActive = false;
            row.IsDefault = false;
            row.DeletedAt = now;
            row.UpdatedAt = now;
            row.EtsyOauthTokenEncrypted = string.Empty;
            row.EtsyRefreshTokenEncrypted = null;
            await integrations.UpdateAsync(row, cancellationToken: cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(InsightsKey(userId), cancellationToken);
        return Result.Success();
    }

    private async Task SaveConnectionAsync(Guid userId, EtsyShopDetails shop, EtsyTokenSet tokens, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await integrations.FindAsync(row => row.UserId == userId, cancellationToken);
        var row = rows.FirstOrDefault(item => item.EtsyShopId == shop.ShopId);

        // One shop is the seller's default; connecting a shop makes it the one in use.
        foreach (var other in rows.Where(item => item != row && item.IsDefault == true))
        {
            other.IsDefault = false;
            other.UpdatedAt = now;
            await integrations.UpdateAsync(other, cancellationToken: cancellationToken);
        }

        if (row is null)
        {
            row = new EtsyIntegration { Id = Guid.NewGuid(), UserId = userId, EtsyShopId = shop.ShopId, CreatedAt = now };
            ApplyConnection(row, shop, tokens, now);
            await integrations.AddAsync(row, cancellationToken: cancellationToken);
        }
        else
        {
            ApplyConnection(row, shop, tokens, now);
            await integrations.UpdateAsync(row, cancellationToken: cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        // A new grant can see more (another shop, new scopes): drop what was computed under the old one.
        await cache.RemoveAsync(InsightsKey(userId), cancellationToken);
    }

    private void ApplyConnection(EtsyIntegration row, EtsyShopDetails shop, EtsyTokenSet tokens, DateTime now)
    {
        row.ShopName = Truncate(shop.ShopName);
        StoreTokens(row, tokens, now);
        row.IsActive = true;
        row.IsDefault = true;
        row.DeletedAt = null;
        row.LastSyncAt = now;
        row.UpdatedAt = now;
    }

    private void StoreTokens(EtsyIntegration row, EtsyTokenSet tokens, DateTime now)
    {
        var expiresAt = tokens.AccessTokenExpiresAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        row.EtsyOauthTokenEncrypted = etsy.Protect($"{expiresAt}:{tokens.AccessToken}");
        row.EtsyRefreshTokenEncrypted = etsy.Protect(tokens.RefreshToken);
        row.OauthExpiresAt = now + RefreshTokenLifetime;
    }

    /// <summary>The stored access token, renewed first when it is about to expire.</summary>
    private async Task<string> AccessTokenAsync(EtsyIntegration integration, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (token, expiresAt) = ReadAccessToken(integration.EtsyOauthTokenEncrypted);
        if (token is not null && expiresAt - RefreshMargin > now)
            return token;

        if (string.IsNullOrEmpty(integration.EtsyRefreshTokenEncrypted))
            throw new EtsyApiException(EtsyFailureKind.Unauthorized, "No refresh token is stored.");

        var refreshed = await etsy.RefreshAsync(etsy.Unprotect(integration.EtsyRefreshTokenEncrypted), cancellationToken);
        StoreTokens(integration, refreshed, now.UtcDateTime);
        integration.UpdatedAt = now.UtcDateTime;
        // Saved before the token is used: Etsy has already retired the old refresh token.
        await integrations.UpdateAsync(integration, saveChange: true, cancellationToken);
        return refreshed.AccessToken;
    }

    private (string? Token, DateTimeOffset ExpiresAt) ReadAccessToken(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return (null, DateTimeOffset.MinValue);
        try
        {
            var value = etsy.Unprotect(encrypted);
            var separator = value.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0 && long.TryParse(value[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                return (value[(separator + 1)..], DateTimeOffset.FromUnixTimeSeconds(seconds));
        }
        catch (CryptographicException)
        {
            // A value the current key cannot read is treated as missing; the refresh token renews it.
        }
        return (null, DateTimeOffset.MinValue);
    }

    private async Task<EtsyIntegration?> FindActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await integrations.FindAsync(
            row => row.UserId == userId && row.DeletedAt == null && row.IsActive != false, cancellationToken);
        return rows.OrderByDescending(row => row.IsDefault == true).ThenByDescending(row => row.UpdatedAt).FirstOrDefault();
    }

    private CompleteEtsyConnectionResponseDto Return(string outcome) => new(etsy.BuildReturnUrl(outcome), outcome);

    private GetMyShopResponseDto Describe(string status, EtsyIntegration integration, ShopDetailsDto shop) =>
        new(status, etsy.IsConfigured, shop,
            integration.CreatedAt is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)) : null,
            integration.LastSyncAt is { } synced ? new DateTimeOffset(DateTime.SpecifyKind(synced, DateTimeKind.Utc)) : null,
            integration.OauthExpiresAt is { } reconnect ? new DateTimeOffset(DateTime.SpecifyKind(reconnect, DateTimeKind.Utc)) : null);

    /// <summary>What is known without calling Etsy: the stored shop id and name.</summary>
    private static ShopDetailsDto StoredShop(EtsyIntegration integration) =>
        new(integration.EtsyShopId, integration.ShopName, null, $"https://www.etsy.com/shop/{Uri.EscapeDataString(integration.ShopName)}",
            null, null, null, null, null, null, null, null, null, null);

    private static ShopDetailsDto Map(EtsyShopDetails shop) =>
        new(shop.ShopId, shop.ShopName, shop.Title, shop.Url, shop.IconUrl, shop.CurrencyCode, shop.ActiveListingCount,
            shop.DigitalListingCount, shop.FavorerCount, shop.SoldCount, shop.ReviewAverage, shop.ReviewCount, shop.IsOnVacation, shop.OpenedAtUtc);

    private static string Truncate(string value) => value.Length <= 255 ? value : value[..255];

    /// <summary>A URL-safe random string from <paramref name="bytes"/> random bytes.</summary>
    private static string RandomToken(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>The PKCE S256 challenge for a verifier.</summary>
    internal static string CodeChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>What the callback needs to finish a connection; kept in the cache under the state.</summary>
    public sealed record PendingEtsyConnection(Guid UserId, string CodeVerifier);
}
