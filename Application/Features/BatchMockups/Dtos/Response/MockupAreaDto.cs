namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>
/// One print area of a mock-up template photo, such as the chest, a sleeve or the back. A template has its main
/// area (key "main") and up to three more.
/// </summary>
/// <param name="Key">Names the area within its template: "main", or a short lowercase key such as "sleeve-left".</param>
/// <param name="Name">What the seller calls it, such as "Left sleeve"; null when not named.</param>
/// <param name="X">Left of the area, in the photo's pixels.</param>
/// <param name="Y">Top of the area.</param>
/// <param name="Width">Width of the area.</param>
/// <param name="Height">Height of the area.</param>
/// <param name="Rotation">Degrees the design is turned about the area's center, clockwise, from -180 to 180.</param>
/// <param name="Source">
/// What is printed there: "same" for the design the mock-up is made of, "design" for the design named by
/// <paramref name="DesignImageId"/>, "none" for nothing. On a template it is the default ("same" or "none"); null
/// takes the default.
/// </param>
/// <param name="DesignImageId">The design printed there when <paramref name="Source"/> is "design".</param>
public sealed record MockupAreaDto(
    string Key,
    string? Name,
    int X,
    int Y,
    int Width,
    int Height,
    double Rotation = 0,
    string? Source = null,
    Guid? DesignImageId = null);

/// <summary>What a batch job prints in one print area of a template.</summary>
/// <param name="Kind">"same", "design" or "none", as <see cref="MockupAreaDto.Source"/>.</param>
/// <param name="DesignImageId">The design printed there when <paramref name="Kind"/> is "design".</param>
public sealed record MockupAreaSourceDto(string Kind, Guid? DesignImageId = null);
