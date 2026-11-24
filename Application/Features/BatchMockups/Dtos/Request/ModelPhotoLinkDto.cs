using APCS.Application.Features.BatchMockups.Common;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>A model photo made with AI, still at the provider's temporary link, to be stored from there.</summary>
public sealed record ModelPhotoLinkDto(string Name, string ProductType, string ImageUrl, ModelAttributesDto Attributes);
