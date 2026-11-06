namespace APCS.Application.Features.ImageQuality.Common;

/// <summary>
/// Reads the small thumbnail an export file carries for an image: a <c>data:image/...;base64,</c> URL whose bytes must
/// really be the JPEG, PNG or WebP it claims to be, and small enough to be a thumbnail.
/// </summary>
public static class ImageEvalThumbnail
{
    /// <summary>The largest thumbnail accepted, in bytes.</summary>
    public const int MaximumBytes = 400 * 1024;

    private static readonly (string MimeType, string Extension)[] Formats =
    [
        ("image/jpeg", "jpg"),
        ("image/png", "png"),
        ("image/webp", "webp"),
    ];

    /// <summary>Decodes a thumbnail, or returns null when it is not an accepted image.</summary>
    public static DecodedThumbnail? Decode(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return null;
        }

        foreach (var (mimeType, extension) in Formats)
        {
            var prefix = $"data:{mimeType};base64,";
            if (!dataUrl.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = dataUrl.AsSpan(prefix.Length);
            var buffer = new byte[(payload.Length * 3 / 4) + 3];
            if (!Convert.TryFromBase64Chars(payload, buffer, out var length) || length == 0 || length > MaximumBytes)
            {
                return null;
            }

            var bytes = buffer.AsSpan(0, length).ToArray();
            return HasSignature(bytes, mimeType) ? new DecodedThumbnail(bytes, mimeType, extension) : null;
        }

        return null;
    }

    private static bool HasSignature(byte[] bytes, string mimeType) => mimeType switch
    {
        "image/jpeg" => bytes is [0xFF, 0xD8, 0xFF, ..],
        "image/png" => bytes is [0x89, 0x50, 0x4E, 0x47, ..],
        "image/webp" => bytes.Length >= 12
            && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        _ => false,
    };
}

/// <summary>A thumbnail's bytes and the type they were checked to be.</summary>
public sealed record DecodedThumbnail(byte[] Bytes, string MimeType, string Extension);
