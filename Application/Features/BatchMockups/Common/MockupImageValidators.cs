using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>Shared base-photo file checks (mirrors <c>StyleArtPresetValidators</c>).</summary>
public static class MockupImageValidators
{
    public static bool IsSupportedImage(UploadFileDto? file) =>
        file is not null && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static bool IsWithinSizeLimit(UploadFileDto? file) =>
        file is not null && file.Length > 0 && file.Length <= MockupRules.MaximumBaseImageBytes;
}
