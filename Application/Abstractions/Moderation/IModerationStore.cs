namespace APCS.Application.Abstractions.Moderation;

/// <summary>
/// Reads the blocklist and writes the moderation log. It works on its own database context, so recording a check never
/// saves (or is rolled back with) changes the calling use case has staged. A database without the moderation tables yet
/// (the Flyway migration has not reached it) reads as empty lists and skips the log.
/// </summary>
public interface IModerationStore
{
    Task<ModerationLists> LoadListsAsync(CancellationToken cancellationToken = default);

    /// <summary>False while the Flyway migration has not created the moderation tables in this database yet.</summary>
    Task<bool> IsStorageReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>The settings an administrator changed (<c>moderation_settings</c>), by key; null when the table does not exist yet.</summary>
    Task<IReadOnlyDictionary<string, string>?> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task AddCheckAsync(ModerationCheckRecord record, CancellationToken cancellationToken = default);

    /// <summary>The decision of the latest output check of a subject, as stored; null when there is none.</summary>
    Task<string?> GetLatestOutputDecisionAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken = default);
}

/// <summary>An active blocklist term (<c>moderation_terms</c>).</summary>
public sealed record ModerationTermEntry(string Term, string Category, string Severity, string MatchType, bool MatchUnaccented);

/// <summary>The active terms and allowlisted phrases.</summary>
public sealed record ModerationLists(IReadOnlyList<ModerationTermEntry> Terms, IReadOnlyList<string> Allowlist)
{
    public static ModerationLists Empty { get; } = new([], []);
}

/// <summary>One row of <c>moderation_checks</c>.</summary>
public sealed record ModerationCheckRecord(
    Guid? UserId,
    string Feature,
    string Direction,
    string SubjectType,
    Guid? SubjectId,
    string ContentHash,
    string Decision,
    string DecidedBy,
    string CategoriesJson,
    string? Reason,
    string? Model,
    int? LatencyMs,
    int PolicyVersion,
    string? ContentExcerpt = null);
