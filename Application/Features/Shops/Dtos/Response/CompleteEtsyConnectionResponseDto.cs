namespace APCS.Application.Features.Shops.Dtos.Response;

/// <summary>Where the seller's browser goes after Etsy calls back, carrying the outcome for the client page.</summary>
public sealed record CompleteEtsyConnectionResponseDto(string RedirectUrl, string Outcome);
