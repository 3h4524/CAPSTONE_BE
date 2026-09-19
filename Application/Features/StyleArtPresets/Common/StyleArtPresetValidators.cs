using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.StyleArtPresets.Common;

namespace APCS.Application.Features.StyleArtPresets.Common;

/// <summary>Shared preview file checks.</summary>
public static class StyleArtPresetValidators
{
    public static bool IsSupportedImage(UploadFileDto? file) =>
        file is not null && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static bool IsWithinSizeLimit(UploadFileDto? file) =>
        file is not null && file.Length > 0 && file.Length <= StyleArtPresetRules.MaximumPreviewBytes;
}
