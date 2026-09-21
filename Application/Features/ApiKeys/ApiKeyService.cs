using System.Data.Common;
using System.Security.Cryptography;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Persistence.Dtos;
using APCS.Application.Common.Validation;
using APCS.Application.Features.ApiKeys.Dtos.Request;
using APCS.Application.Features.ApiKeys.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;

namespace APCS.Application.Features.ApiKeys;

public sealed class ApiKeyService(
    ICurrentUser currentUser,
    IAccountService accounts,
    TimeProvider timeProvider,
    IApiKeyRepository repository,
    IApiKeyCredentials credentials,
    IValidator<SaveApiKeyRequestDto> validator) : IApiKeyService
{
    public async Task<Result<ListApiKeysResponseDto>> ListMineAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<ListApiKeysResponseDto>(
                Error.Unauthorized("ApiKeys.Unauthenticated", "Please sign in to view API keys."));

        try
        {
            var account = await accounts.FindByIdAsync(userId, cancellationToken);
            if (account is null || !account.IsActive)
                return Forbidden();

            var roles = await accounts.GetRolesAsync(userId, cancellationToken);
            if (!roles.Contains(AuthConstants.UserRole, StringComparer.Ordinal))
                return Forbidden();

            var metadata = await repository.ListMetadataOwnedAsync(userId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var items = metadata.Select(key => Map(key, now)).ToArray();
            var connected = items.Where(key => key.Status == "Connected").ToArray();
            // One connected service for each stage of the core creation/publishing pipeline.
            var ready = new[] { "Image generation", "Listing content", "Product publishing" }
                .All(category => connected.Any(key => key.ProviderCategory == category));

            return Result.Success(new ListApiKeysResponseDto(
                items, connected.Length, items.Length - connected.Length, ready,
                items.Select(key => key.LastCheckedAtUtc).DefaultIfEmpty().Max()));
        }
        catch (DbException)
        {
            return Result.Failure<ListApiKeysResponseDto>(Error.Failure(
                "MSG16", "Unable to load API keys. Please refresh the page to try again."));
        }
        catch (TimeoutException)
        {
            return Result.Failure<ListApiKeysResponseDto>(Error.Failure(
                "MSG16", "Unable to load API keys. Please refresh the page to try again."));
        }
    }

    public async Task<Result<IReadOnlyList<ApiKeyProviderResponseDto>>> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        if (await AuthorizeManagementAsync(cancellationToken) is { } error)
            return Result.Failure<IReadOnlyList<ApiKeyProviderResponseDto>>(error);
        return Result.Success<IReadOnlyList<ApiKeyProviderResponseDto>>(new ApiKeyProviderResponseDto[]
        {
            new("openai", "OpenAI", true, ["Read models (connection check)", "Model inference for listing content", "No billing, admin or fine-tuning permissions needed"]),
            new("replicate", "Replicate", true, ["Read account (connection check)", "Run predictions for image generation"]),
            new("printify", "Printify", true, ["Read shops (connection check)", "Manage and publish products", "No order or payment access needed"]),
            new("etsy", "Etsy", false, ["Etsy requires an OAuth account connection; API key entry is not supported."])
        });
    }

    private static Error MissingKey => Error.NotFound("ApiKeys.NotFound", "This connection no longer exists.");
    private static Error CheckFailed => new("MSG50", "Unable to validate this key. Check its permissions and expiry, or try again later.", ErrorType.Validation);
    private static Error Duplicate => Error.Conflict("MSG51", "This provider is already connected. Edit or delete its existing key.");

    private async Task<Error?> AuthorizeManagementAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Error.Unauthorized("ApiKeys.Unauthenticated", "Please sign in to manage API keys.");
        var account = await accounts.FindByIdAsync(userId, cancellationToken);
        if (account is null || !account.IsActive ||
            !(await accounts.GetRolesAsync(userId, cancellationToken)).Contains(AuthConstants.UserRole, StringComparer.Ordinal))
            return Error.Forbidden("ApiKeys.Forbidden", "Only active Seller accounts can manage API keys.");
        return null;
    }

    private bool IsConnected(ApiKey key) => key.IsActive == true && key.LastCheckSucceeded == true &&
        key.LastCheckedAt.HasValue && (!key.ExpiresAt.HasValue || key.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime);

    public async Task<Result> SaveAsync(Guid? id, SaveApiKeyRequestDto request, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeManagementAsync(cancellationToken) is { } error) return Result.Failure(error);
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var validationError = validation.ToValidationError();
            if (validation.Errors[0].ErrorCode.StartsWith("MSG", StringComparison.Ordinal))
                validationError = validationError with { Code = validation.Errors[0].ErrorCode };
            return Result.Failure(validationError);
        }
        if (!id.HasValue && string.IsNullOrEmpty(request.ApiKey))
            return Result.Failure(new Error("MSG01", "Enter an API key.", ErrorType.Validation));
        var userId = currentUser.UserId!.Value;
        await using var transaction = await repository.LockOwnerAsync(userId, cancellationToken);
        var owned = await repository.ListOwnedAsync(userId, cancellationToken);
        var key = id.HasValue ? owned.SingleOrDefault(x => x.Id == id) : null;
        if (id.HasValue && key is null) return Result.Failure(MissingKey);
        if (key is not null && (!string.Equals(key.ServiceProvider, request.Provider, StringComparison.OrdinalIgnoreCase) || key.AuthType == "oauth"))
            return Result.Failure(Error.Validation("The provider cannot be changed. Add a new connection instead."));
        if (owned.Any(x => x.Id != id && string.Equals(x.ServiceProvider.Trim(), request.Provider, StringComparison.OrdinalIgnoreCase) && IsConnected(x)))
            return Result.Failure(Duplicate);

        if (request.ApiKey is not null && !await credentials.ValidateAsync(request.Provider, request.ApiKey, cancellationToken))
            return Result.Failure(CheckFailed);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var added = key is null;
        key ??= new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ServiceProvider = request.Provider,
            AuthType = "api_key",
            CreatedAt = now,
            UsageCount = 0
        };
        key.KeyIdentifier = string.IsNullOrWhiteSpace(request.Name) ? request.Provider : request.Name.Trim();
        key.Environment = request.Environment;
        if (request.ApiKey is not null)
        {
            key.KeyValueEncrypted = credentials.Protect(request.ApiKey);
            key.KeyLast4 = request.ApiKey[^4..];
            key.IsActive = true;
            key.LastCheckedAt = now;
            key.LastCheckSucceeded = true;
            key.ExpiresAt = null;
            if (added) key.LastUsedAt = now;
        }
        if (added) await repository.AddAsync(key, true, cancellationToken);
        else await repository.UpdateAsync(key, true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeManagementAsync(cancellationToken) is { } error) return Result.Failure(error);
        var userId = currentUser.UserId!.Value;
        await using var transaction = await repository.LockOwnerAsync(userId, cancellationToken);
        var key = (await repository.ListOwnedAsync(userId, cancellationToken)).SingleOrDefault(x => x.Id == id);
        if (key is null) return Result.Failure(MissingKey);
        key.DeletedAt = timeProvider.GetUtcNow().UtcDateTime;
        key.IsActive = false;
        key.KeyValueEncrypted = "";
        key.KeyLast4 = null;
        await repository.UpdateAsync(key, true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ValidateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeManagementAsync(cancellationToken) is { } error) return Result.Failure(error);
        var userId = currentUser.UserId!.Value;
        await using var transaction = await repository.LockOwnerAsync(userId, cancellationToken);
        var owned = await repository.ListOwnedAsync(userId, cancellationToken);
        var key = owned.SingleOrDefault(x => x.Id == id);
        if (key is null) return Result.Failure(MissingKey);
        if (key.AuthType == "oauth") return Result.Failure(Error.Validation("Reconnect this account through OAuth."));
        if (owned.Any(x => x.Id != id && string.Equals(x.ServiceProvider.Trim(), key.ServiceProvider.Trim(), StringComparison.OrdinalIgnoreCase) && IsConnected(x)))
            return Result.Failure(Duplicate);
        bool valid;
        try
        {
            valid = await credentials.ValidateAsync(key.ServiceProvider.Trim().ToLowerInvariant(), credentials.Unprotect(key.KeyValueEncrypted), cancellationToken);
        }
        catch (CryptographicException) { valid = false; }
        if (key.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime) valid = false;
        key.LastCheckedAt = timeProvider.GetUtcNow().UtcDateTime;
        key.LastCheckSucceeded = valid;
        key.IsActive = valid;
        await repository.UpdateAsync(key, true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return valid ? Result.Success() : Result.Failure(CheckFailed);
    }

    private static Result<ListApiKeysResponseDto> Forbidden() =>
        Result.Failure<ListApiKeysResponseDto>(Error.Forbidden(
            "ApiKeys.Forbidden", "Only active Seller accounts can view API keys."));

    private static ApiKeyResponseDto Map(ApiKeyMetadata key, DateTimeOffset now)
    {
        var provider = key.Provider.Trim().ToLowerInvariant() switch
        {
            "openai" => ("OpenAI", "Listing content"),
            "replicate" => ("Replicate", "Image generation"),
            "printify" => ("Printify", "Product publishing"),
            "etsy" => ("Etsy", "Listing publishing"),
            _ => (key.Provider, "Other service")
        };
        var oauth = string.Equals(key.AuthType, "oauth", StringComparison.OrdinalIgnoreCase);
        var status = key.IsActive == false || key.LastCheckSucceeded == false ||
            (key.ExpiresAt is DateTime expires && ToUtc(expires) <= now)
                ? "Reconnect"
                : key.IsActive == true && key.LastCheckSucceeded == true && key.LastCheckedAt.HasValue
                    ? "Connected" : "Needs attention";
        // A short/malformed suffix is fully masked. Never use/decrypt KeyValueEncrypted.
        var credential = oauth
            ? string.IsNullOrWhiteSpace(key.ConnectedAccountName) ? "Connected account" : key.ConnectedAccountName
            : key.Last4 is { Length: 4 } last4 ? "****" + last4 : "****";

        return new ApiKeyResponseDto(
            key.Id, provider.Item1, provider.Item2, oauth ? "OAuth" : "API Key",
            key.Label, credential, key.Environment, status,
            ToUtc(key.LastUsedAt), ToUtc(key.LastCheckedAt));
    }

    private static DateTimeOffset? ToUtc(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
}
