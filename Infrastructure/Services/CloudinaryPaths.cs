namespace APCS.Infrastructure.Services;

/// <summary>Shared Cloudinary public-id and folder handling.</summary>
internal static class CloudinaryPaths
{
    internal static string BuildPublicId(string folder, string storageKey)
    {
        var normalizedFolder = NormalizePath(folder);
        var key = NormalizePath(storageKey);
        return string.IsNullOrWhiteSpace(normalizedFolder) ? key : $"{normalizedFolder}/{key}";
    }

    internal static string BuildAssetFolder(string folder, string storageKey)
    {
        var publicId = BuildPublicId(folder, storageKey);
        var lastSeparatorIndex = publicId.LastIndexOf('/');
        return lastSeparatorIndex < 0 ? string.Empty : publicId[..lastSeparatorIndex];
    }

    internal static string NormalizePath(string value) =>
        string.Join('/', value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
