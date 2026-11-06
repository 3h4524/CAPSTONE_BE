using System.Globalization;
using APCS.Api.Extensions;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.BatchMockups;

/// <summary>Multipart form used to create or change a model photo.</summary>
public sealed class SaveModelPhotoForm
{
    public string Name { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>The photo of a person; optional when changing a model photo.</summary>
    public IFormFile? Photo { get; set; }

    /// <summary>A PNG whose alpha is the garment on the photo.</summary>
    public IFormFile? GarmentSelection { get; set; }

    /// <summary>Who is in the photo: 'single' or 'group'; empty keeps what it had.</summary>
    public string? People { get; set; }

    /// <summary>For one person: 'child', 'teen', 'adult' or 'senior'.</summary>
    public string? AgeGroup { get; set; }

    /// <summary>For one person: 'female' or 'male'.</summary>
    public string? Gender { get; set; }

    /// <summary>For a group: 'family', 'friends' or 'couple'.</summary>
    public string? GroupType { get; set; }

    /// <summary>Find the garment on the photo automatically; the selection is then not needed.</summary>
    public bool FindGarment { get; set; }

    /// <summary>Place each design by measuring the garments instead of in the print area.</summary>
    public bool AutoPlacement { get; set; }

    /// <summary>
    /// The print area's corners as eight numbers, "x,y" from the top-left clockwise; empty keeps it a rectangle.
    /// </summary>
    public string? PrintQuad { get; set; }

    /// <summary>The request the service takes. The caller disposes the files' streams.</summary>
    public SaveModelPhotoRequestDto ToRequest() =>
        new(Name, ProductType, X, Y, Width, Height, Photo.ToUploadFileDto(), GarmentSelection.ToUploadFileDto(), AutoPlacement, ParseQuad(PrintQuad), FindGarment,
            string.IsNullOrWhiteSpace(People) ? null : new APCS.Application.Features.BatchMockups.Common.ModelAttributesDto(People, AgeGroup, Gender, GroupType));

    // Anything but eight numbers comes through as no corners at all, which the validator refuses.
    private static IReadOnlyList<MockupPoint>? ParseQuad(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var numbers = value.Split(',').Select(part => double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN).ToArray();
        if (numbers.Length != 8 || numbers.Any(double.IsNaN))
            return [];
        return [new(numbers[0], numbers[1]), new(numbers[2], numbers[3]), new(numbers[4], numbers[5]), new(numbers[6], numbers[7])];
    }
}
