namespace APCS.Application.Features.Shops.Dtos.Response;

/// <summary>The Etsy consent page the client sends the seller to.</summary>
public sealed record StartEtsyConnectionResponseDto(string AuthorizationUrl);
