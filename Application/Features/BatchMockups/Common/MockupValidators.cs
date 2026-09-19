using System.Text.Json;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;

namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>Shared mock-up template file and payload checks.</summary>
public static class MockupValidators
{
    public static bool IsSupportedImage(UploadFileDto? file) =>
        file is not null && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static bool IsWithinSizeLimit(UploadFileDto? file) =>
        file is not null && file.Length > 0 && file.Length <= 5 * 1024 * 1024;

    public static bool IsJsonObject(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        try
        {
            using var document = JsonDocument.Parse(source);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
