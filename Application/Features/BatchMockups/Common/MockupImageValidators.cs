using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>Shared base-photo file checks (mirrors <c>StyleArtPresetValidators</c>).</summary>
public static class MockupImageValidators
{
    public static bool IsSupportedImage(UploadFileDto? file) =>
        file is not null && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static bool IsWithinSizeLimit(UploadFileDto? file) =>
        file is not null && file.Length > 0 && file.Length <= MockupRules.MaximumBaseImageBytes;

    /// <summary>
    /// Whether the photo is a PNG, JPEG or WebP by its first bytes, whatever content type it was sent
    /// with. Only those are analyzed as uploaded; any other format is analyzed from the JPEG the CDN
    /// makes of it once it is stored.
    /// </summary>
    public static bool IsDirectlyReadable(ReadOnlySpan<byte> photo) =>
        photo.StartsWith(PngSignature)
        || photo.StartsWith(JpegSignature)
        || (photo.Length >= 12 && photo[..4].SequenceEqual("RIFF"u8) && photo.Slice(8, 4).SequenceEqual("WEBP"u8));

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
}
