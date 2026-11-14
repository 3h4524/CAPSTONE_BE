namespace APCS.Application.Features.Admin.Moderation.Dtos;

// ---- Requests ----

/// <summary>Filters of the moderation log. Dates are UTC; <c>To</c> is exclusive.</summary>
public sealed record ListModerationChecksRequestDto(
    int Page = 1,
    int PageSize = 20,
    string? Decision = null,
    string? DecidedBy = null,
    string? Feature = null,
    string? Direction = null,
    string? Category = null,
    string? Search = null,
    DateTime? From = null,
    DateTime? To = null);

public sealed record ListModerationTermsRequestDto(
    string? Search = null,
    string? Category = null,
    string? Severity = null,
    bool? Active = null);

/// <param name="Term">A word or phrase; stored lower-case with single spaces.</param>
/// <param name="MatchType"><c>word</c> or <c>phrase</c>; a term with a space is always a phrase.</param>
/// <param name="MatchUnaccented">Also match once accents are removed; only when the accent-free spelling is no everyday word.</param>
public sealed record SaveModerationTermRequestDto(
    string Term,
    string Category,
    string Severity,
    string MatchType = "word",
    bool MatchUnaccented = false,
    string Language = "en",
    bool IsActive = true,
    string? Note = null);

public sealed record SaveModerationAllowlistRequestDto(string Phrase, bool IsActive = true, string? Note = null);

/// <param name="Enabled">Whether the Gemini check runs; the blocklist always does.</param>
/// <param name="BlockAt">The lowest rating that blocks, by category: <c>high</c> or <c>medium</c>.</param>
public sealed record UpdateModerationSettingsRequestDto(bool Enabled, IReadOnlyDictionary<string, string> BlockAt);

// ---- Responses ----

public sealed record ModerationTotalsDto(
    int Checks,
    int Allowed,
    int Flagged,
    int Blocked,
    int Unchecked,
    double BlockRate,
    int? AverageLatencyMs);

public sealed record ModerationDailyPointDto(DateOnly Date, int Allowed, int Flagged, int Blocked, int Unchecked);

public sealed record ModerationCategoryCountDto(string Category, int Blocked, int Flagged);

public sealed record ModerationCountDto(string Key, int Count);

public sealed record ModerationFeatureCountDto(string Feature, int Checks, int Blocked);

/// <param name="StorageReady">False while the moderation tables do not exist in this database yet.</param>
/// <param name="ByDecider">Blocks only, by the layer that blocked.</param>
public sealed record ModerationOverviewDto(
    bool StorageReady,
    int Days,
    ModerationTotalsDto Totals,
    IReadOnlyList<ModerationDailyPointDto> Daily,
    IReadOnlyList<ModerationCategoryCountDto> ByCategory,
    IReadOnlyList<ModerationCountDto> ByDecider,
    IReadOnlyList<ModerationFeatureCountDto> ByFeature,
    IReadOnlyList<ModerationCountDto> ByDirection);

public sealed record ModerationOffenderDto(
    Guid UserId,
    string Email,
    string FullName,
    int Blocked,
    int Flagged,
    DateTime LastBlockedAt,
    string? TopCategory);

public sealed record ModerationFindingDto(string Category, string Severity, string? Field, string? Evidence);

/// <param name="ExcerptPreview">The start of the kept text; null for an allowed check, whose text is not kept.</param>
public sealed record ModerationCheckListItemDto(
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
    IReadOnlyList<string> Categories,
    string? ExcerptPreview,
    int? LatencyMs);

public sealed record ModerationCheckDetailDto(
    Guid Id,
    DateTime CreatedAt,
    Guid? UserId,
    string? UserEmail,
    string? UserName,
    string Feature,
    string Direction,
    string SubjectType,
    Guid? SubjectId,
    string Decision,
    string DecidedBy,
    IReadOnlyList<ModerationFindingDto> Findings,
    string? Reason,
    string? ContentExcerpt,
    string? Model,
    int? LatencyMs,
    int PolicyVersion,
    string ContentHash);

public sealed record ModerationTermDto(
    Guid Id,
    string Term,
    string Category,
    string Severity,
    string MatchType,
    bool MatchUnaccented,
    string Language,
    bool IsActive,
    string? Note,
    DateTime UpdatedAt);

public sealed record ModerationAllowlistDto(Guid Id, string Phrase, bool IsActive, string? Note, DateTime UpdatedAt);

/// <param name="Locked">Minors in a sexual context always block; their threshold cannot change.</param>
public sealed record ModerationThresholdDto(string Category, string BlockAt, bool Locked);

/// <param name="ConfigEnabled">The <c>Moderation:Enabled</c> setting; when false Gemini never runs, whatever is set here.</param>
/// <param name="GeminiConfigured">Whether the platform's Gemini key is set.</param>
public sealed record ModerationSettingsDto(
    bool StorageReady,
    bool ConfigEnabled,
    bool GeminiConfigured,
    bool Enabled,
    IReadOnlyList<ModerationThresholdDto> Thresholds,
    string Model,
    int PolicyVersion,
    DateTime? UpdatedAt,
    string? UpdatedByEmail);
