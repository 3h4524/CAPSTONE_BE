using System.Globalization;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Moderation;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Persistence.Dtos;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Admin.Moderation.Common;
using APCS.Application.Features.Admin.Moderation.Dtos;
using APCS.Application.Features.Moderation;
using APCS.Application.Features.Moderation.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Admin.Moderation;

/// <inheritdoc />
public sealed class AdminModerationService(
    IRepository<ModerationCheck> checks,
    IRepository<ModerationTerm> terms,
    IRepository<ModerationAllowlist> allowlist,
    IRepository<ModerationSetting> settings,
    IRepository<User> users,
    IRepository<AuditLog> auditLogs,
    IModerationCheckRepository checkSearch,
    IModerationStore store,
    ModerationBlocklistCache blocklist,
    ModerationRuntimeSettings runtimeSettings,
    IGeminiTextGenerationProvider gemini,
    IOptions<ModerationOptions> options,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    IValidator<SaveModerationTermRequestDto> termValidator,
    IValidator<SaveModerationAllowlistRequestDto> allowlistValidator,
    IValidator<UpdateModerationSettingsRequestDto> settingsValidator) : IAdminModerationService
{
    private const string Block = "block";
    private const string Flagged = "flagged";
    private const int ExcerptPreviewLength = 160;
    private static readonly int[] RangeDays = [7, 30, 90];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<ModerationOverviewDto>> GetOverviewAsync(int days, CancellationToken cancellationToken = default)
    {
        days = RangeDays.Contains(days) ? days : 30;
        var firstDay = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(1 - days);
        var empty = new ModerationOverviewDto(false, days, new ModerationTotalsDto(0, 0, 0, 0, 0, 0, null),
            DailyPoints(firstDay, days, []), [], [], [], []);
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Success(empty);

        var since = firstDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var inRange = checks.Query().Where(check => check.CreatedAt >= since);

        var perDay = await inRange
            .GroupBy(check => new { Day = check.CreatedAt.Date, check.Decision })
            .Select(group => new DayCount(group.Key.Day, group.Key.Decision, group.Count()))
            .ToListAsync(cancellationToken);
        var averageLatency = await inRange.Where(check => check.LatencyMs != null)
            .AverageAsync(check => (double?)check.LatencyMs, cancellationToken);
        var byDecider = await inRange.Where(check => check.Decision == Block)
            .GroupBy(check => check.DecidedBy)
            .Select(group => new ModerationCountDto(group.Key, group.Count()))
            .ToListAsync(cancellationToken);
        var byFeature = await inRange
            .GroupBy(check => check.Feature)
            .Select(group => new ModerationFeatureCountDto(group.Key, group.Count(), group.Count(check => check.Decision == Block)))
            .ToListAsync(cancellationToken);
        var byDirection = await inRange
            .GroupBy(check => check.Direction)
            .Select(group => new ModerationCountDto(group.Key, group.Count()))
            .ToListAsync(cancellationToken);
        // The categories sit in a jsonb array; only the checks that were not allowed have any.
        var withFindings = await inRange
            .Where(check => check.Decision == Block || check.Decision == Flagged)
            .Select(check => new { check.Decision, check.Categories })
            .ToListAsync(cancellationToken);

        var byCategory = withFindings
            .SelectMany(check => CategoriesOf(check.Categories).Select(category => (category, check.Decision)))
            .GroupBy(item => item.category)
            .Select(group => new ModerationCategoryCountDto(group.Key,
                group.Count(item => item.Decision == Block), group.Count(item => item.Decision == Flagged)))
            .OrderByDescending(item => item.Blocked + item.Flagged)
            .ToList();

        int Count(string decision) => perDay.Where(item => item.Decision == decision).Sum(item => item.Count);
        var total = perDay.Sum(item => item.Count);
        var totals = new ModerationTotalsDto(total, Count("allow"), Count(Flagged), Count(Block), Count("unchecked"),
            total == 0 ? 0 : Math.Round((double)Count(Block) / total, 4),
            averageLatency is null ? null : (int)Math.Round(averageLatency.Value));

        return Result.Success(new ModerationOverviewDto(true, days, totals, DailyPoints(firstDay, days, perDay), byCategory,
            byDecider.OrderByDescending(item => item.Count).ToList(),
            byFeature.OrderByDescending(item => item.Checks).ToList(),
            byDirection.OrderByDescending(item => item.Count).ToList()));
    }

    public async Task<Result<IReadOnlyList<ModerationOffenderDto>>> GetTopOffendersAsync(int days, int limit,
        CancellationToken cancellationToken = default)
    {
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Success<IReadOnlyList<ModerationOffenderDto>>([]);
        days = RangeDays.Contains(days) ? days : 30;
        limit = Math.Clamp(limit, 1, 50);
        var since = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(1 - days).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var concerns = checks.Query().Where(check => check.CreatedAt >= since && check.UserId != null &&
                                                     (check.Decision == Block || check.Decision == Flagged));

        var ranked = await concerns
            .GroupBy(check => check.UserId!.Value)
            .Select(group => new
            {
                UserId = group.Key,
                Blocked = group.Count(check => check.Decision == Block),
                Flagged = group.Count(check => check.Decision == Flagged),
                LastBlockedAt = group.Max(check => check.Decision == Block ? check.CreatedAt : (DateTime?)null)
            })
            .Where(item => item.Blocked > 0)
            .OrderByDescending(item => item.Blocked).ThenByDescending(item => item.Flagged)
            .Take(limit)
            .ToListAsync(cancellationToken);
        if (ranked.Count == 0) return Result.Success<IReadOnlyList<ModerationOffenderDto>>([]);

        var ids = ranked.Select(item => item.UserId).ToList();
        var people = await users.Query().Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, user.Email, user.FullName })
            .ToDictionaryAsync(user => user.Id, cancellationToken);
        var blockedCategories = await concerns.Where(check => check.Decision == Block && ids.Contains(check.UserId!.Value))
            .Select(check => new { UserId = check.UserId!.Value, check.Categories })
            .ToListAsync(cancellationToken);
        var topCategory = blockedCategories
            .SelectMany(check => CategoriesOf(check.Categories).Select(category => (check.UserId, category)))
            .GroupBy(item => item.UserId)
            .ToDictionary(group => group.Key,
                group => group.GroupBy(item => item.category).OrderByDescending(byCategory => byCategory.Count()).First().Key);

        return Result.Success<IReadOnlyList<ModerationOffenderDto>>(ranked.Select(item => new ModerationOffenderDto(
            item.UserId,
            people.TryGetValue(item.UserId, out var person) ? person.Email : "(deleted account)",
            person?.FullName ?? string.Empty,
            item.Blocked,
            item.Flagged,
            item.LastBlockedAt ?? DateTime.MinValue,
            topCategory.GetValueOrDefault(item.UserId))).ToList());
    }

    public async Task<Result<PagedResult<ModerationCheckListItemDto>>> ListChecksAsync(ListModerationChecksRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var page = await checkSearch.SearchAsync(new ModerationCheckSearch(request.Page, request.PageSize,
            Blank(request.Decision), Blank(request.DecidedBy), Blank(request.Feature), Blank(request.Direction),
            Blank(request.Category), Blank(request.Search), ToUtc(request.From), ToUtc(request.To)), cancellationToken);

        return Result.Success(new PagedResult<ModerationCheckListItemDto>(
            page.Items.Select(row => new ModerationCheckListItemDto(row.Id, row.CreatedAt, row.UserId, row.UserEmail,
                row.UserName, row.Feature, row.Direction, row.SubjectType, row.Decision, row.DecidedBy,
                CategoriesOf(row.Categories),
                row.ContentExcerpt is { Length: > ExcerptPreviewLength } excerpt
                    ? excerpt[..(ExcerptPreviewLength - 1)] + "…"
                    : row.ContentExcerpt,
                row.LatencyMs)).ToList(),
            page.TotalCount, page.PageNumber, page.PageSize));
    }

    public async Task<Result<ModerationCheckDetailDto>> GetCheckAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Failure<ModerationCheckDetailDto>(AdminModerationErrors.CheckNotFound());
        var check = await checks.Query().Where(item => item.Id == id).SingleOrDefaultAsync(cancellationToken);
        if (check is null) return Result.Failure<ModerationCheckDetailDto>(AdminModerationErrors.CheckNotFound());
        var owner = check.UserId is Guid userId
            ? await users.Query().Where(user => user.Id == userId)
                .Select(user => new { user.Email, user.FullName }).SingleOrDefaultAsync(cancellationToken)
            : null;

        return Result.Success(new ModerationCheckDetailDto(check.Id, check.CreatedAt, check.UserId, owner?.Email, owner?.FullName,
            check.Feature, check.Direction, check.SubjectType, check.SubjectId, check.Decision, check.DecidedBy,
            FindingsOf(check.Categories), check.Reason, check.ContentExcerpt, check.Model, check.LatencyMs,
            check.PolicyVersion, check.ContentHash.Trim()));
    }

    public async Task<Result<IReadOnlyList<ModerationTermDto>>> ListTermsAsync(ListModerationTermsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Success<IReadOnlyList<ModerationTermDto>>([]);
        var query = terms.Query();
        if (Blank(request.Search) is { } search)
        {
            var normalized = NormalizePhrase(search);
            query = query.Where(term => term.Term.Contains(normalized));
        }
        if (Blank(request.Category) is { } category) query = query.Where(term => term.Category == category);
        if (Blank(request.Severity) is { } severity) query = query.Where(term => term.Severity == severity);
        if (request.Active is bool active) query = query.Where(term => term.IsActive == active);

        var list = await query.OrderBy(term => term.Category).ThenBy(term => term.Term)
            .Select(term => new ModerationTermDto(term.Id, term.Term, term.Category, term.Severity, term.MatchType,
                term.MatchUnaccented, term.Language, term.IsActive, term.Note, term.UpdatedAt))
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ModerationTermDto>>(list);
    }

    public async Task<Result<ModerationTermDto>> CreateTermAsync(SaveModerationTermRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await termValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result.Failure<ModerationTermDto>(validation.ToValidationError());
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Failure<ModerationTermDto>(AdminModerationErrors.StorageMissing());

        var normalized = NormalizePhrase(request.Term);
        if (await terms.Query().AnyAsync(term => term.Term == normalized, cancellationToken))
            return Result.Failure<ModerationTermDto>(AdminModerationErrors.DuplicateTerm());

        var now = time.GetUtcNow().UtcDateTime;
        var term = new ModerationTerm
        {
            Id = Guid.NewGuid(),
            CreatedAt = now
        };
        Apply(term, request, normalized, now);
        await terms.AddAsync(term, cancellationToken: cancellationToken);
        await AuditAsync("CreateModerationTerm", "ModerationTerm", term.Id, null, TermDto(term), now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        blocklist.Invalidate();
        return Result.Success(TermDto(term));
    }

    public async Task<Result<ModerationTermDto>> UpdateTermAsync(Guid id, SaveModerationTermRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await termValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result.Failure<ModerationTermDto>(validation.ToValidationError());
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Failure<ModerationTermDto>(AdminModerationErrors.StorageMissing());

        var term = await terms.GetByIdAsync(id, cancellationToken);
        if (term is null) return Result.Failure<ModerationTermDto>(AdminModerationErrors.TermNotFound());
        var normalized = NormalizePhrase(request.Term);
        if (await terms.Query().AnyAsync(other => other.Id != id && other.Term == normalized, cancellationToken))
            return Result.Failure<ModerationTermDto>(AdminModerationErrors.DuplicateTerm());

        var before = TermDto(term);
        var now = time.GetUtcNow().UtcDateTime;
        Apply(term, request, normalized, now);
        await terms.UpdateAsync(term, cancellationToken: cancellationToken);
        await AuditAsync("UpdateModerationTerm", "ModerationTerm", term.Id, before, TermDto(term), now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        blocklist.Invalidate();
        return Result.Success(TermDto(term));
    }

    public async Task<Result> DeleteTermAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Failure(AdminModerationErrors.StorageMissing());
        var term = await terms.GetByIdAsync(id, cancellationToken);
        if (term is null) return Result.Failure(AdminModerationErrors.TermNotFound());

        await terms.RemoveAsync(term, cancellationToken: cancellationToken);
        await AuditAsync("DeleteModerationTerm", "ModerationTerm", term.Id, TermDto(term), null,
            time.GetUtcNow().UtcDateTime, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        blocklist.Invalidate();
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ModerationAllowlistDto>>> ListAllowlistAsync(CancellationToken cancellationToken = default)
    {
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Success<IReadOnlyList<ModerationAllowlistDto>>([]);
        var list = await allowlist.Query().OrderBy(item => item.Phrase)
            .Select(item => new ModerationAllowlistDto(item.Id, item.Phrase, item.IsActive, item.Note, item.UpdatedAt))
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ModerationAllowlistDto>>(list);
    }

    public async Task<Result<ModerationAllowlistDto>> CreateAllowlistAsync(SaveModerationAllowlistRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await allowlistValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result.Failure<ModerationAllowlistDto>(validation.ToValidationError());
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Failure<ModerationAllowlistDto>(AdminModerationErrors.StorageMissing());

        var phrase = NormalizePhrase(request.Phrase);
        if (await allowlist.Query().AnyAsync(item => item.Phrase == phrase, cancellationToken))
            return Result.Failure<ModerationAllowlistDto>(AdminModerationErrors.DuplicateAllowlist());

        var now = time.GetUtcNow().UtcDateTime;
        var item = new ModerationAllowlist
        {
            Id = Guid.NewGuid(),
            Phrase = phrase,
            IsActive = request.IsActive,
            Note = Blank(request.Note),
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = currentUser.UserId
        };
        await allowlist.AddAsync(item, cancellationToken: cancellationToken);
        await AuditAsync("CreateModerationAllowlist", "ModerationAllowlist", item.Id, null, AllowlistDto(item), now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        blocklist.Invalidate();
        return Result.Success(AllowlistDto(item));
    }

    public async Task<Result<ModerationAllowlistDto>> UpdateAllowlistAsync(Guid id, SaveModerationAllowlistRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await allowlistValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result.Failure<ModerationAllowlistDto>(validation.ToValidationError());
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Failure<ModerationAllowlistDto>(AdminModerationErrors.StorageMissing());

        var item = await allowlist.GetByIdAsync(id, cancellationToken);
        if (item is null) return Result.Failure<ModerationAllowlistDto>(AdminModerationErrors.AllowlistNotFound());
        var phrase = NormalizePhrase(request.Phrase);
        if (await allowlist.Query().AnyAsync(other => other.Id != id && other.Phrase == phrase, cancellationToken))
            return Result.Failure<ModerationAllowlistDto>(AdminModerationErrors.DuplicateAllowlist());

        var before = AllowlistDto(item);
        var now = time.GetUtcNow().UtcDateTime;
        item.Phrase = phrase;
        item.IsActive = request.IsActive;
        item.Note = Blank(request.Note);
        item.UpdatedAt = now;
        item.UpdatedBy = currentUser.UserId;
        await allowlist.UpdateAsync(item, cancellationToken: cancellationToken);
        await AuditAsync("UpdateModerationAllowlist", "ModerationAllowlist", item.Id, before, AllowlistDto(item), now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        blocklist.Invalidate();
        return Result.Success(AllowlistDto(item));
    }

    public async Task<Result> DeleteAllowlistAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Failure(AdminModerationErrors.StorageMissing());
        var item = await allowlist.GetByIdAsync(id, cancellationToken);
        if (item is null) return Result.Failure(AdminModerationErrors.AllowlistNotFound());

        await allowlist.RemoveAsync(item, cancellationToken: cancellationToken);
        await AuditAsync("DeleteModerationAllowlist", "ModerationAllowlist", item.Id, AllowlistDto(item), null,
            time.GetUtcNow().UtcDateTime, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        blocklist.Invalidate();
        return Result.Success();
    }

    public async Task<Result<ModerationSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var ready = await store.IsStorageReadyAsync(cancellationToken);
        var rows = ready
            ? await settings.Query().Select(row => new { row.Key, row.Value, row.UpdatedAt, row.UpdatedBy }).ToListAsync(cancellationToken)
            : [];
        var snapshot = ModerationSettingsSnapshot.From(rows.ToDictionary(row => row.Key, row => row.Value));
        var latest = rows.OrderByDescending(row => row.UpdatedAt).FirstOrDefault();
        var updatedBy = latest?.UpdatedBy is Guid userId
            ? await users.Query().Where(user => user.Id == userId).Select(user => user.Email).SingleOrDefaultAsync(cancellationToken)
            : null;

        var thresholds = ModerationCategories.All.Select(category => category == ModerationCategories.SexualMinors
                ? new ModerationThresholdDto(category, "any", true)
                : new ModerationThresholdDto(category,
                    snapshot.BlockFrom(category) == ModerationSeverity.Medium ? "medium" : "high", false))
            .ToList();
        var config = options.Value;
        return Result.Success(new ModerationSettingsDto(ready, config.Enabled, gemini.IsConfigured, snapshot.Enabled,
            thresholds, config.Model, config.PolicyVersion, latest?.UpdatedAt, updatedBy));
    }

    public async Task<Result<ModerationSettingsDto>> UpdateSettingsAsync(UpdateModerationSettingsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await settingsValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result.Failure<ModerationSettingsDto>(validation.ToValidationError());
        if (!await store.IsStorageReadyAsync(cancellationToken)) return Result.Failure<ModerationSettingsDto>(AdminModerationErrors.StorageMissing());

        var wanted = new Dictionary<string, string>
        {
            [ModerationSettingKeys.Enabled] = request.Enabled ? "true" : "false"
        };
        foreach (var (category, value) in request.BlockAt) wanted[ModerationSettingKeys.BlockAt(category)] = value;

        var now = time.GetUtcNow().UtcDateTime;
        var before = new Dictionary<string, string>();
        foreach (var (key, value) in wanted)
        {
            var row = await settings.GetByIdAsync(key, cancellationToken);
            if (row is null)
            {
                await settings.AddAsync(new ModerationSetting { Key = key, Value = value, UpdatedAt = now, UpdatedBy = currentUser.UserId },
                    cancellationToken: cancellationToken);
                continue;
            }
            if (row.Value == value) continue;
            before[key] = row.Value;
            row.Value = value;
            row.UpdatedAt = now;
            row.UpdatedBy = currentUser.UserId;
            await settings.UpdateAsync(row, cancellationToken: cancellationToken);
        }

        await AuditAsync("UpdateModerationSettings", "ModerationSettings", null, before, wanted, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        runtimeSettings.Invalidate();
        return await GetSettingsAsync(cancellationToken);
    }

    private void Apply(ModerationTerm term, SaveModerationTermRequestDto request, string normalized, DateTime now)
    {
        term.Term = normalized;
        term.Category = request.Category;
        term.Severity = request.Severity;
        // A term of several words can only ever match as a phrase.
        term.MatchType = normalized.Contains(' ') ? "phrase" : request.MatchType;
        term.MatchUnaccented = request.MatchUnaccented;
        term.Language = request.Language;
        term.IsActive = request.IsActive;
        term.Note = Blank(request.Note);
        term.UpdatedAt = now;
        term.UpdatedBy = currentUser.UserId;
    }

    private async Task AuditAsync(string action, string resourceType, Guid? resourceId, object? oldValue, object? newValue,
        DateTime now, CancellationToken cancellationToken) =>
        await auditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = currentUser.UserId,
            ActionType = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue, Json),
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue, Json),
            CreatedAt = now
        }, cancellationToken: cancellationToken);

    private static ModerationTermDto TermDto(ModerationTerm term) => new(term.Id, term.Term, term.Category, term.Severity,
        term.MatchType, term.MatchUnaccented, term.Language, term.IsActive, term.Note, term.UpdatedAt);

    private static ModerationAllowlistDto AllowlistDto(ModerationAllowlist item) =>
        new(item.Id, item.Phrase, item.IsActive, item.Note, item.UpdatedAt);

    // Every day of the range, oldest first, with the days without a check at zero.
    private static List<ModerationDailyPointDto> DailyPoints(DateOnly firstDay, int days, IReadOnlyList<DayCount> counts) =>
        Enumerable.Range(0, days).Select(offset =>
        {
            var day = firstDay.AddDays(offset);
            int Count(string decision) => counts
                .Where(item => DateOnly.FromDateTime(item.Day) == day && item.Decision == decision)
                .Sum(item => item.Count);
            return new ModerationDailyPointDto(day, Count("allow"), Count(Flagged), Count(Block), Count("unchecked"));
        }).ToList();

    internal static IReadOnlyList<string> CategoriesOf(string? json) =>
        FindingsOf(json).Select(finding => finding.Category).Distinct().ToList();

    internal static IReadOnlyList<ModerationFindingDto> FindingsOf(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ModerationFindingDto>>(json, Json)?
                .Where(finding => !string.IsNullOrWhiteSpace(finding.Category))
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Lower case, composed accents, single spaces: the form the blocklist stores and matches.
    internal static string NormalizePhrase(string value) =>
        string.Join(' ', value.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? ToUtc(DateTime? value) => value is null
        ? null
        : value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value.ToUniversalTime();

    private sealed record DayCount(DateTime Day, string Decision, int Count);
}
