namespace APCS.Application.Abstractions.Storage;

/// <summary>
/// Represents a file persisted by the configured storage provider.
/// </summary>
public sealed record StoredFileDto(string StorageKey, string StableUrl);

/// <summary>
/// Represents a temporary authorized download link.
/// </summary>
public sealed record SignedDownloadDto(string Url, DateTimeOffset ExpiresAtUtc);
