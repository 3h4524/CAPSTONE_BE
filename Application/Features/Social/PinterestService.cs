using System.Security.Cryptography;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Social;
using APCS.Application.Features.Social.Common;
using APCS.Application.Features.Social.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Social;

/// <summary>A seller's Pinterest account: connecting it, its boards, and posting pins to one of them.</summary>
public interface IPinterestService
{
    Task<Result<StartPinterestConnectionResponseDto>> StartConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Finishes the consent page's round trip; always answers with where to send the browser.</summary>
    Task<CompletePinterestConnectionResponseDto> CompleteConnectionAsync(string? code, string? state, string? error, CancellationToken cancellationToken = default);

    Task<Result<PinterestConnectionResponseDto>> GetConnectionAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PinterestBoardDto>>> GetBoardsAsync(CancellationToken cancellationToken = default);

    Task<Result> DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts image pins of a product of <paramref name="userId"/> to one of their boards, for a workflow step running
    /// in the background. A mock-up already pinned to that board is not pinned again, so a retried step carries on.
    /// </summary>
    Task<Result<PostPinsResult>> PostPinsAsync(Guid userId, Guid productId, string boardId, IReadOnlyList<PinToPost> pins, CancellationToken cancellationToken = default);
}

/// <summary>
/// Connects Pinterest with OAuth 2.0 (the app authenticates with its secret, so no PKCE) and posts pins as the seller.
/// </summary>
/// <remarks>
/// Pinterest access tokens live about 30 days and refresh tokens about a year; the access token is renewed silently
/// before use, and <c>refresh_expires_at</c> is when the seller must connect again. Each posted pin is a
/// <c>social_media_shares</c> row naming its mock-up and board, saved as soon as Pinterest accepts it.
/// </remarks>
public sealed class PinterestService(
    ICurrentUser currentUser,
    ICacheService cache,
    IPinterestClient pinterest,
    IRepository<PinterestIntegration> integrations,
    IRepository<SocialMediaShare> shares,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<PinterestService> logger) : IPinterestService
{
    public const string Platform = "pinterest";

    /// <summary>How long a seller has to finish Pinterest's consent page.</summary>
    internal static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    /// <summary>An access token this close to expiry is renewed before use.</summary>
    internal static readonly TimeSpan RefreshMargin = TimeSpan.FromDays(1);

    internal static string StateKey(string state) => $"pinterest:oauth:state:{state}";

    public async Task<Result<StartPinterestConnectionResponseDto>> StartConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<StartPinterestConnectionResponseDto>(SocialErrors.Unauthenticated());
        if (!pinterest.IsConfigured)
            return Result.Failure<StartPinterestConnectionResponseDto>(SocialErrors.PinterestNotConfigured());

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        await cache.SetAsync(StateKey(state), new PendingPinterestConnection(userId), StateLifetime, cancellationToken);
        return Result.Success(new StartPinterestConnectionResponseDto(pinterest.BuildAuthorizationUrl(state)));
    }

    public async Task<CompletePinterestConnectionResponseDto> CompleteConnectionAsync(string? code, string? state, string? error, CancellationToken cancellationToken = default)
    {
        PendingPinterestConnection? pending = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            pending = await cache.GetAsync<PendingPinterestConnection>(StateKey(state), cancellationToken);
            // One use only: a replayed callback finds nothing.
            await cache.RemoveAsync(StateKey(state), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(error))
            return Return(pending is null ? PinterestConnectionOutcomes.Expired : PinterestConnectionOutcomes.Denied);
        if (pending is null || string.IsNullOrWhiteSpace(code))
            return Return(PinterestConnectionOutcomes.Expired);

        try
        {
            var tokens = await pinterest.ExchangeCodeAsync(code, cancellationToken);
            var username = await pinterest.GetUsernameAsync(tokens.AccessToken, cancellationToken);
            await SaveConnectionAsync(pending.UserId, username, tokens, cancellationToken);
            return Return(PinterestConnectionOutcomes.Connected);
        }
        catch (PinterestApiException exception)
        {
            logger.LogWarning("Pinterest connection for user {UserId} failed: {Kind}.", pending.UserId, exception.Kind);
            return Return(PinterestConnectionOutcomes.Failed);
        }
    }

    public async Task<Result<PinterestConnectionResponseDto>> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<PinterestConnectionResponseDto>(SocialErrors.Unauthenticated());

        var row = await FindActiveAsync(userId, cancellationToken);
        if (row is null)
            return Result.Success(new PinterestConnectionResponseDto(pinterest.IsConfigured, PinterestConnectionStatuses.NotConnected, null, null, null));

        var now = timeProvider.GetUtcNow();
        var status = CanStillRefresh(row, now) || Utc(row.AccessExpiresAt) > now
            ? PinterestConnectionStatuses.Connected
            : PinterestConnectionStatuses.ReconnectRequired;
        return Result.Success(new PinterestConnectionResponseDto(pinterest.IsConfigured, status, row.PinterestUsername,
            Utc(row.CreatedAt), Utc(row.RefreshExpiresAt)));
    }

    public async Task<Result<IReadOnlyList<PinterestBoardDto>>> GetBoardsAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<IReadOnlyList<PinterestBoardDto>>(SocialErrors.Unauthenticated());
        var row = await FindActiveAsync(userId, cancellationToken);
        if (row is null)
            return Result.Failure<IReadOnlyList<PinterestBoardDto>>(SocialErrors.PinterestNotConnected());

        try
        {
            var token = await AccessTokenAsync(row, cancellationToken);
            var boards = await pinterest.GetBoardsAsync(token, cancellationToken);
            return Result.Success<IReadOnlyList<PinterestBoardDto>>(boards
                .OrderBy(board => board.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(board => new PinterestBoardDto(board.Id, board.Name, board.Privacy))
                .ToArray());
        }
        catch (PinterestApiException exception)
        {
            return Result.Failure<IReadOnlyList<PinterestBoardDto>>(Map(exception));
        }
    }

    public async Task<Result> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure(SocialErrors.Unauthenticated());

        var rows = await integrations.FindAsync(row => row.UserId == userId && row.DeletedAt == null, cancellationToken);
        if (rows.Count == 0)
            return Result.Failure(SocialErrors.PinterestNotConnected());

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var row in rows)
        {
            // The tokens are blanked, not kept: a disconnected account must not be usable from a stored token.
            row.DeletedAt = now;
            row.UpdatedAt = now;
            row.AccessTokenEncrypted = string.Empty;
            row.RefreshTokenEncrypted = null;
            await integrations.UpdateAsync(row, cancellationToken: cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PostPinsResult>> PostPinsAsync(Guid userId, Guid productId, string boardId, IReadOnlyList<PinToPost> pins, CancellationToken cancellationToken = default)
    {
        if (!pinterest.IsConfigured)
            return Result.Failure<PostPinsResult>(SocialErrors.PinterestNotConfigured());
        var row = await FindActiveAsync(userId, cancellationToken);
        if (row is null)
            return Result.Failure<PostPinsResult>(SocialErrors.PinterestNotConnected());

        var mockupIds = pins.Select(pin => pin.MockupImageId).ToArray();
        var earlier = await shares.Query()
            .Where(share => share.ProductId == productId && share.Platform == Platform && share.DestinationId == boardId
                && share.ShareStatus == "posted" && share.DeletedAt == null
                && share.MockupImageId != null && mockupIds.Contains(share.MockupImageId.Value))
            .Select(share => new { MockupImageId = share.MockupImageId!.Value, share.ExternalPostId, share.ExternalPlatformUrl })
            .ToListAsync(cancellationToken);
        var pinned = earlier.GroupBy(share => share.MockupImageId).ToDictionary(group => group.Key, group => group.First());

        var posted = new List<PostedPin>();
        var failures = new List<string>();
        string token;
        try
        {
            token = await AccessTokenAsync(row, cancellationToken);
        }
        catch (PinterestApiException exception)
        {
            return Result.Failure<PostPinsResult>(Map(exception));
        }

        foreach (var (pin, index) in pins.Select((pin, index) => (pin, index)))
        {
            if (pinned.TryGetValue(pin.MockupImageId, out var done))
            {
                posted.Add(new PostedPin(pin.MockupImageId, done.ExternalPostId ?? string.Empty, done.ExternalPlatformUrl ?? string.Empty, AlreadyPosted: true));
                continue;
            }

            try
            {
                var created = await pinterest.CreatePinAsync(token,
                    new PinterestNewPin(boardId, pin.ImageUrl, pin.Title, pin.Description, pin.Link, pin.AltText), cancellationToken);
                await RecordAsync(productId, pin, boardId, "posted", created, null, cancellationToken);
                posted.Add(new PostedPin(pin.MockupImageId, created.PinId, created.Url, AlreadyPosted: false));
            }
            catch (PinterestApiException exception)
            {
                logger.LogWarning("Pin {Index} of product {ProductId} was not posted: {Kind}.", index + 1, productId, exception.Kind);
                var message = exception.Reason is { Length: > 0 } reason ? $"Pinterest refused pin {index + 1}: {reason}" : Map(exception).Message;
                await RecordAsync(productId, pin, boardId, "failed", null, message, cancellationToken);
                // A lost grant or a rate limit stops every later pin too: say so once instead of failing each one.
                if (exception.Kind == PinterestFailureKind.Unauthorized)
                    return posted.Count == 0
                        ? Result.Failure<PostPinsResult>(SocialErrors.PinterestReconnectRequired())
                        : Result.Success(new PostPinsResult(posted, [.. failures, SocialErrors.PinterestReconnectRequired().Message]));
                failures.Add(message);
                if (exception.Kind == PinterestFailureKind.RateLimited)
                    break;
            }
        }
        return Result.Success(new PostPinsResult(posted, failures));
    }

    private async Task RecordAsync(Guid productId, PinToPost pin, string boardId, string status, PinterestCreatedPin? created, string? error, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Saved at once, outside any transaction: the pin exists on Pinterest whatever happens to the step next.
        await shares.AddAsync(new SocialMediaShare
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            MockupImageId = pin.MockupImageId,
            Platform = Platform,
            DestinationId = boardId,
            ShareStatus = status,
            PostedTime = created is null ? null : now,
            PostCaption = pin.Description,
            ExternalPostId = created?.PinId,
            ExternalPlatformUrl = created?.Url,
            ErrorMessage = error,
            RetryCount = 0,
            CreatedAt = now,
        }, saveChange: true, cancellationToken);
    }

    private async Task SaveConnectionAsync(Guid userId, string username, PinterestTokenSet tokens, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var row = await FindActiveAsync(userId, cancellationToken);
        if (row is null)
        {
            row = new PinterestIntegration { Id = Guid.NewGuid(), UserId = userId, CreatedAt = now };
            Apply(row, username, tokens, now);
            await integrations.AddAsync(row, cancellationToken: cancellationToken);
        }
        else
        {
            // Connecting again (another account, or after a lost grant) replaces the account in use.
            Apply(row, username, tokens, now);
            await integrations.UpdateAsync(row, cancellationToken: cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private void Apply(PinterestIntegration row, string username, PinterestTokenSet tokens, DateTime now)
    {
        row.PinterestUsername = username.Length <= 255 ? username : username[..255];
        StoreTokens(row, tokens);
        row.UpdatedAt = now;
        row.DeletedAt = null;
    }

    private void StoreTokens(PinterestIntegration row, PinterestTokenSet tokens)
    {
        row.AccessTokenEncrypted = pinterest.Protect(tokens.AccessToken);
        row.AccessExpiresAt = tokens.AccessTokenExpiresAtUtc.UtcDateTime;
        // Pinterest may keep the refresh token on a refresh: the stored one then stays in use.
        if (tokens.RefreshToken is { Length: > 0 } refresh)
        {
            row.RefreshTokenEncrypted = pinterest.Protect(refresh);
            row.RefreshExpiresAt = tokens.RefreshTokenExpiresAtUtc?.UtcDateTime;
        }
    }

    /// <summary>The stored access token, renewed first when it is about to expire.</summary>
    private async Task<string> AccessTokenAsync(PinterestIntegration row, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var token = ReadToken(row.AccessTokenEncrypted);
        if (token is not null && Utc(row.AccessExpiresAt) is { } expiresAt && expiresAt - RefreshMargin > now)
            return token;

        if (string.IsNullOrEmpty(row.RefreshTokenEncrypted) || ReadToken(row.RefreshTokenEncrypted) is not { } refreshToken)
            return token ?? throw new PinterestApiException(PinterestFailureKind.Unauthorized, "No refresh token is stored.");

        var refreshed = await pinterest.RefreshAsync(refreshToken, cancellationToken);
        StoreTokens(row, refreshed);
        row.UpdatedAt = now.UtcDateTime;
        await integrations.UpdateAsync(row, saveChange: true, cancellationToken);
        return refreshed.AccessToken;
    }

    /// <summary>A stored token, or null when it is empty or the current key cannot read it.</summary>
    private string? ReadToken(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return null;
        try
        {
            return pinterest.Unprotect(encrypted);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static bool CanStillRefresh(PinterestIntegration row, DateTimeOffset now) =>
        !string.IsNullOrEmpty(row.RefreshTokenEncrypted) && (Utc(row.RefreshExpiresAt) is not { } end || end > now);

    private async Task<PinterestIntegration?> FindActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await integrations.FindAsync(row => row.UserId == userId && row.DeletedAt == null, cancellationToken);
        return rows.OrderByDescending(row => row.UpdatedAt).FirstOrDefault();
    }

    private static Error Map(PinterestApiException exception) => exception.Kind switch
    {
        PinterestFailureKind.Unauthorized => SocialErrors.PinterestReconnectRequired(),
        PinterestFailureKind.RateLimited => SocialErrors.PinterestRateLimited(),
        PinterestFailureKind.Rejected when exception.Reason is { Length: > 0 } reason =>
            Error.Failure("social.pinterest_rejected", $"Pinterest refused the request: {reason}"),
        _ => SocialErrors.PinterestUnavailable(),
    };

    private CompletePinterestConnectionResponseDto Return(string outcome) => new(pinterest.BuildReturnUrl(outcome), outcome);

    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } time ? new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)) : null;

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>What the callback needs to finish a connection; kept in the cache under the state.</summary>
    public sealed record PendingPinterestConnection(Guid UserId);
}
