namespace APCS.Application.Features.ListingContent.Dtos;

public sealed record ListingContentTagDto(string Value, string? Type, int Position);

public sealed record ListingContentResponseDto(
    Guid ProductId,
    Guid ListingContentId,
    string Title,
    string Description,
    IReadOnlyList<ListingContentTagDto> Tags,
    int VersionNumber,
    string Model,
    DateTime? CreatedAt);
