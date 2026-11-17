using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Admin.SystemApiKeys.Common;
using APCS.Application.Features.Admin.SystemApiKeys.Dtos.Request;
using APCS.Application.Features.Admin.SystemApiKeys.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Admin.SystemApiKeys;

/// <summary>
/// The Admin's page of the platform's own AI provider keys: which keys there are, how far each is used today, and how
/// far each provider's keys may be used. Every change answers with the whole page, records who made it (never the key),
/// and has the keys read again; other servers read them within a minute.
/// </summary>
public interface ISystemApiKeyAdminService
{
    /// <summary>Every provider with its settings and keys.</summary>
    Task<Result<AdminSystemApiKeysDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a key the provider accepts.</summary>
    Task<Result<AdminSystemApiKeysDto>> AddKeyAsync(AddSystemApiKeyRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Renames a key, or switches it on or off.</summary>
    Task<Result<AdminSystemApiKeysDto>> UpdateKeyAsync(Guid id, UpdateSystemApiKeyRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a key, and the key itself with it.</summary>
    Task<Result<AdminSystemApiKeysDto>> DeleteKeyAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Checks a key with its provider again.</summary>
    Task<Result<AdminSystemApiKeysDto>> CheckKeyAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Puts a key the provider refused today back in use.</summary>
    Task<Result<AdminSystemApiKeysDto>> ClearRefusedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Changes how far each of a provider's keys is used.</summary>
    Task<Result<AdminSystemApiKeysDto>> UpdateProviderAsync(string provider, UpdateSystemApiKeyProviderRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the order fill takes a provider's keys in.</summary>
    Task<Result<AdminSystemApiKeysDto>> ReorderAsync(string provider, ReorderSystemApiKeysRequestDto request,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class SystemApiKeyAdminService(
    IRepository<SystemApiKey> keys,
    IRepository<SystemApiKeyProvider> providers,
    IRepository<AuditLog> auditLogs,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ISystemApiKeySecrets secrets,
    IApiKeyCredentials credentials,
    ISystemApiKeyPool pool,
    IValidator<AddSystemApiKeyRequestDto> addValidator,
    IValidator<UpdateSystemApiKeyRequestDto> updateValidator,
    IValidator<UpdateSystemApiKeyProviderRequestDto> providerValidator) : ISystemApiKeyAdminService
{
    private const string KeyResource = "SystemApiKey";
    private const string ProviderResource = "SystemApiKeyProvider";
    private const string UndefinedTable = "42P01";

    /// <inheritdoc />
    public async Task<Result<AdminSystemApiKeysDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        List<SystemApiKeyProvider> settings;
        try
        {
            settings = await providers.Query().ToListAsync(cancellationToken);
        }
        catch (DbException error) when (error.SqlState == UndefinedTable)
        {
            // The migration has not reached this database yet: the page says so instead of failing.
            return Result.Success(new AdminSystemApiKeysDto([], StorageReady: false));
        }
        var rows = await keys.Query()
            .Where(key => key.DeletedAt == null)
            .OrderBy(key => key.SortOrder).ThenBy(key => key.CreatedAt)
            .ToListAsync(cancellationToken);

        var page = new List<AdminSystemApiKeyProviderDto>();
        foreach (var provider in settings.OrderBy(row => Rank(row.Provider)).ThenBy(row => row.Provider, StringComparer.Ordinal))
        {
            var providerKeys = rows.Where(key => key.Provider == provider.Provider).ToList();
            var usage = await pool.GetUsageTodayAsync(provider.Provider, providerKeys.Select(key => key.KeyId).ToList(), cancellationToken);
            page.Add(new AdminSystemApiKeyProviderDto(
                provider.Provider,
                SystemApiKeyProviders.DisplayName(provider.Provider),
                provider.DailyLimitPerKey,
                provider.MaxConcurrentPerKey,
                provider.Strategy,
                provider.UpdatedAt,
                providerKeys.Select(key => ToDto(key, usage.GetValueOrDefault(key.KeyId))).ToList()));
        }
        return Result.Success(new AdminSystemApiKeysDto(page, StorageReady: true));
    }

    /// <inheritdoc />
    public async Task<Result<AdminSystemApiKeysDto>> AddKeyAsync(AddSystemApiKeyRequestDto request, CancellationToken cancellationToken = default)
    {
        var validation = await addValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result.Failure<AdminSystemApiKeysDto>(validation.ToValidationError());

        var provider = request.Provider;
        if (!await providers.Query().AnyAsync(row => row.Provider == provider, cancellationToken))
            return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.ProviderNotFound());

        var value = request.Key.Trim();
        var keyId = SystemApiKeyIds.Of(value);
        if (await keys.Query().AnyAsync(key => key.Provider == provider && key.KeyId == keyId && key.DeletedAt == null, cancellationToken))
            return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.Duplicate());

        // Only a key the provider accepts is saved, so a typo never reaches the images.
        if (!await credentials.ValidateAsync(provider, value, cancellationToken))
            return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.Refused(SystemApiKeyProviders.DisplayName(provider)));

        var lastOrder = await keys.Query()
            .Where(key => key.Provider == provider && key.DeletedAt == null)
            .Select(key => (int?)key.SortOrder)
            .MaxAsync(cancellationToken);
        var now = Now();
        var row = new SystemApiKey
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Label = request.Label.Trim(),
            KeyValueEncrypted = secrets.Protect(value),
            KeyLast4 = value[^4..],
            KeyId = keyId,
            SortOrder = (lastOrder ?? -1) + 1,
            IsActive = true,
            LastCheckedAt = now,
            LastCheckSucceeded = true,
            UpdatedBy = currentUser.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await keys.AddAsync(row, cancellationToken: cancellationToken);
        await AuditAsync("AddSystemApiKey", KeyResource, row.Id, null, Snapshot(row), now, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another Admin added the same key a moment ago: the unique index caught it.
            return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.Duplicate());
        }
        return await ChangedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AdminSystemApiKeysDto>> UpdateKeyAsync(Guid id, UpdateSystemApiKeyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var row = await FindKeyAsync(id, cancellationToken);
        if (row is null) return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.KeyNotFound());

        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result.Failure<AdminSystemApiKeysDto>(validation.ToValidationError());
        if (!SameTime(row.UpdatedAt, request.UpdatedAt))
            return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.ChangedByAnotherAdmin());

        var before = Snapshot(row);
        var now = Now();
        row.Label = request.Label.Trim();
        row.IsActive = request.IsActive;
        row.UpdatedAt = now;
        row.UpdatedBy = currentUser.UserId;
        await keys.UpdateAsync(row, cancellationToken: cancellationToken);
        await AuditAsync("UpdateSystemApiKey", KeyResource, row.Id, before, Snapshot(row), now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ChangedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AdminSystemApiKeysDto>> DeleteKeyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await FindKeyAsync(id, cancellationToken);
        if (row is null) return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.KeyNotFound());

        var before = Snapshot(row);
        var now = Now();
        // The row stays for the record; the key itself goes.
        row.DeletedAt = now;
        row.IsActive = false;
        row.KeyValueEncrypted = string.Empty;
        row.UpdatedAt = now;
        row.UpdatedBy = currentUser.UserId;
        await keys.UpdateAsync(row, cancellationToken: cancellationToken);
        await AuditAsync("DeleteSystemApiKey", KeyResource, row.Id, before, null, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ChangedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AdminSystemApiKeysDto>> CheckKeyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await FindKeyAsync(id, cancellationToken);
        if (row is null) return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.KeyNotFound());
        if (TryDecrypt(row.KeyValueEncrypted) is not { } value)
            return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.Unreadable());

        // The answer is shown on the key; a refused key stays as it is until an Admin switches it off or deletes it.
        row.LastCheckSucceeded = await credentials.ValidateAsync(row.Provider, value, cancellationToken);
        row.LastCheckedAt = Now();
        await keys.UpdateAsync(row, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AdminSystemApiKeysDto>> ClearRefusedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await FindKeyAsync(id, cancellationToken);
        if (row is null) return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.KeyNotFound());

        await pool.ClearRefusedTodayAsync(row.Provider, row.KeyId, cancellationToken);
        await AuditAsync("ClearSystemApiKeyRefusal", KeyResource, row.Id, null, Snapshot(row), Now(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AdminSystemApiKeysDto>> UpdateProviderAsync(string provider, UpdateSystemApiKeyProviderRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var row = await providers.Query().FirstOrDefaultAsync(settings => settings.Provider == provider, cancellationToken);
        if (row is null) return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.ProviderNotFound());

        var validation = await providerValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result.Failure<AdminSystemApiKeysDto>(validation.ToValidationError());
        if (!SameTime(row.UpdatedAt, request.UpdatedAt))
            return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.ChangedByAnotherAdmin());

        var before = Snapshot(row);
        var now = Now();
        row.DailyLimitPerKey = request.DailyLimitPerKey;
        row.MaxConcurrentPerKey = request.MaxConcurrentPerKey;
        row.Strategy = request.Strategy;
        row.UpdatedAt = now;
        row.UpdatedBy = currentUser.UserId;
        await providers.UpdateAsync(row, cancellationToken: cancellationToken);
        await AuditAsync("UpdateSystemApiKeyProvider", ProviderResource, null, before, Snapshot(row), now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ChangedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AdminSystemApiKeysDto>> ReorderAsync(string provider, ReorderSystemApiKeysRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!await providers.Query().AnyAsync(row => row.Provider == provider, cancellationToken))
            return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.ProviderNotFound());

        var rows = await keys.Query()
            .Where(key => key.Provider == provider && key.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var wanted = request.KeyIds ?? [];
        if (wanted.Count != rows.Count || wanted.Distinct().Count() != wanted.Count || !rows.All(row => wanted.Contains(row.Id)))
            return Result.Failure<AdminSystemApiKeysDto>(SystemApiKeyAdminErrors.OrderMismatch());

        var before = rows.OrderBy(row => row.SortOrder).ThenBy(row => row.CreatedAt).Select(row => row.Label).ToList();
        foreach (var row in rows)
        {
            var order = wanted.ToList().IndexOf(row.Id);
            if (row.SortOrder == order) continue;
            row.SortOrder = order;
            await keys.UpdateAsync(row, cancellationToken: cancellationToken);
        }
        var after = wanted.Select(id => rows.First(row => row.Id == id).Label).ToList();
        await AuditAsync("ReorderSystemApiKeys", ProviderResource, null, new { provider, order = before }, new { provider, order = after },
            Now(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ChangedAsync(cancellationToken);
    }

    // The keys are read again on this server now, and on the others within a minute.
    private async Task<Result<AdminSystemApiKeysDto>> ChangedAsync(CancellationToken cancellationToken)
    {
        pool.Invalidate();
        return await GetAsync(cancellationToken);
    }

    private Task<SystemApiKey?> FindKeyAsync(Guid id, CancellationToken cancellationToken) =>
        keys.Query().FirstOrDefaultAsync(key => key.Id == id && key.DeletedAt == null, cancellationToken);

    private AdminSystemApiKeyDto ToDto(SystemApiKey key, SystemApiKeyUsageToday? usage) => new(
        key.Id,
        key.Provider,
        key.Label,
        $"****{key.KeyLast4}",
        key.KeyId,
        key.IsActive,
        usage?.Used ?? 0,
        usage?.RefusedReason,
        TryDecrypt(key.KeyValueEncrypted) is not null,
        key.LastCheckedAt,
        key.LastCheckSucceeded,
        key.CreatedAt,
        key.UpdatedAt);

    private string? TryDecrypt(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return null;
        try
        {
            return secrets.Unprotect(encrypted);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private Task AuditAsync(string action, string resource, Guid? resourceId, object? before, object? after, DateTime now,
        CancellationToken cancellationToken) =>
        auditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = currentUser.UserId,
            ActionType = action,
            ResourceType = resource,
            ResourceId = resourceId,
            OldValue = before is null ? null : JsonSerializer.Serialize(before),
            NewValue = after is null ? null : JsonSerializer.Serialize(after),
            CreatedAt = now,
        }, cancellationToken: cancellationToken);

    // What the record keeps of a key: never the key, only its last four characters.
    private static object Snapshot(SystemApiKey key) =>
        new { provider = key.Provider, label = key.Label, key = $"****{key.KeyLast4}", keyId = key.KeyId, isActive = key.IsActive };

    private static object Snapshot(SystemApiKeyProvider settings) =>
        new { provider = settings.Provider, settings.DailyLimitPerKey, settings.MaxConcurrentPerKey, settings.Strategy };

    private static int Rank(string provider)
    {
        var index = SystemApiKeyProviders.All.ToList().IndexOf(provider);
        return index < 0 ? int.MaxValue : index;
    }

    // The database keeps microseconds: a time cut to them reads back exactly as it was written.
    private DateTime Now()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }

    private static bool SameTime(DateTime stored, DateTime sent)
    {
        var a = stored.Kind == DateTimeKind.Local ? stored.ToUniversalTime() : stored;
        var b = sent.Kind == DateTimeKind.Local ? sent.ToUniversalTime() : sent;
        return a.Ticks / 10 == b.Ticks / 10;
    }
}
