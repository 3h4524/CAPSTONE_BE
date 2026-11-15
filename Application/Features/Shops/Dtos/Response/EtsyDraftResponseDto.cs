namespace APCS.Application.Features.Shops.Dtos.Response;

/// <summary>Everything the push form needs: the product's content, its approved media and the shop's choices.</summary>
/// <param name="ConnectionStatus">As in <see cref="GetMyShopResponseDto.Status"/>; anything but <c>connected</c> means the form cannot be sent.</param>
/// <param name="Defaults">The settings of the seller's last push, or sensible ones for a first push.</param>
public sealed record EtsyDraftResponseDto(
    Guid ProductId,
    string ProductName,
    string ConnectionStatus,
    string? ShopName,
    string? Currency,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<EtsyDraftMockupDto> Mockups,
    EtsyDraftVideoDto? Video,
    EtsyListingSettingsDto Defaults,
    IReadOnlyList<EtsyOptionDto> ShippingProfiles,
    IReadOnlyList<EtsyOptionDto> ProcessingProfiles,
    IReadOnlyList<EtsyOptionDto> ProductionPartners,
    IReadOnlyList<EtsyUploadHistoryDto> History,
    IReadOnlyList<ShopDesignDto> Designs);

/// <param name="AiMade">Made or edited with AI: Etsy wants a physical listing to have a photo besides such ones.</param>
public sealed record EtsyDraftMockupDto(Guid Id, string PreviewUrl, int Width, int Height, bool AiMade = false);

/// <summary>A design of the product that can be sold as a file or printed by Printify.</summary>
/// <param name="IsFinal">The design the seller marked as the final one.</param>
public sealed record ShopDesignDto(Guid Id, string PreviewUrl, int Width, int Height, string FileFormat, decimal FileSizeMb, bool IsFinal);

public sealed record EtsyDraftVideoDto(Guid Id, int DurationSeconds, string? ThumbnailUrl);

public sealed record EtsyOptionDto(long Id, string Label);

/// <summary>The listing settings APCS has no data for; remembered from the seller's last push.</summary>
public sealed record EtsyListingSettingsDto(
    decimal? Price,
    int Quantity,
    long? TaxonomyId,
    string? TaxonomyPath,
    string Type,
    string WhoMade,
    string WhenMade,
    long? ShippingProfileId,
    long? ReadinessStateId,
    IReadOnlyList<long> ProductionPartnerIds);

/// <summary>An earlier push of this product.</summary>
public sealed record EtsyUploadHistoryDto(string Status, string? ListingId, string? EditUrl, DateTimeOffset AttemptedAtUtc, string? Error);

public sealed record EtsyTaxonomyOptionDto(long Id, string Path);

/// <param name="AlreadyPublished">The same content was pushed before; the existing draft is returned instead of a second one.</param>
/// <param name="Warnings">What did not go through (an image or the video), while the draft itself was created.</param>
public sealed record PublishEtsyDraftResponseDto(
    string ListingId,
    string EditUrl,
    int ImagesUploaded,
    bool VideoUploaded,
    bool AlreadyPublished,
    IReadOnlyList<string> Warnings,
    int FilesUploaded = 0);
