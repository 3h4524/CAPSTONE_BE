namespace APCS.Application.Abstractions.Persistence.Dtos;

/// <summary>A page of the moderation log, filtered. Every filter is optional; dates are UTC and <c>To</c> is exclusive.</summary>
public sealed record ModerationCheckSearch(
    int Page,
    int PageSize,
    string? Decision = null,
    string? DecidedBy = null,
    string? Feature = null,
    string? Direction = null,
    string? Category = null,
    string? Seller = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

/// <summary>One row of the moderation log with the seller it belongs to, when known.</summary>
public sealed record ModerationCheckRow(
    Guid Id,
    DateTime CreatedAt,
    Guid? UserId,
    string? UserEmail,
    string? UserName,
    string Feature,
    string Direction,
    string SubjectType,
    string Decision,
    string DecidedBy,
    string Categories,
    string? ContentExcerpt,
    int? LatencyMs);
