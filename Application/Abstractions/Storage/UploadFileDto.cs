namespace APCS.Application.Abstractions.Storage;

/// <summary>
/// Represents a validated HTTP upload passed to an application use case.
/// </summary>
public sealed record UploadFileDto(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);
