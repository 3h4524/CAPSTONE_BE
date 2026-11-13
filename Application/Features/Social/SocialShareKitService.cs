using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.Shops.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Social;

/// <summary>A file of a post, sized for its platform: a link to show it and a link that downloads it.</summary>
public sealed record SocialMediaDto(string Type, string PreviewUrl, string DownloadUrl, int Width, int Height, string FileName);

/// <summary>
/// One post ready to publish by hand on a platform. <c>Kind</c> is <c>image</c>, <c>video</c> or <c>carousel</c>;
/// <c>Title</c>, <c>Link</c> and <c>AltText</c> are Pinterest's (TikTok has none of them).
/// </summary>
public sealed record SocialPostDto(
    string Platform, string Kind, IReadOnlyList<SocialMediaDto> Media, string? Title, string Caption,
    IReadOnlyList<string> Hashtags, string? Link, string? AltText);

/// <summary>Everything a seller needs to promote a product on social media: posts per platform and what to fix first.</summary>
public sealed record SocialShareKitDto(
    Guid ProductId, string ProductName, string? ListingUrl, IReadOnlyList<SocialPostDto> Posts, IReadOnlyList<string> Warnings);

public interface ISocialShareKitService
{
    /// <summary>Pinterest and TikTok posts of a product, from its approved mock-ups, video and listing.</summary>
    Task<Result<SocialShareKitDto>> GetAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>The same kit for a product of <paramref name="userId"/>, for a workflow step running in the background.</summary>
    Task<Result<SocialShareKitDto>> BuildAsync(Guid productId, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds a product's share kit from what the seller already approved: mock-ups and the promo video, the listing's
/// title, description and tags, and the Etsy listing a pin links to. Images are padded to each platform's size so the
/// design stays whole. Nothing is posted: the seller downloads the files and copies the text.
/// </summary>
public sealed class SocialShareKitService(
    ICurrentUser currentUser,
    IRepository<Product> products,
    IRepository<MockupImage> mockups,
    IRepository<PromoVideo> videos,
    IRepository<EtsyUploadLog> etsyUploads,
    IRepository<PrintifyUploadLog> printifyUploads,
    IListingContentService listingContent,
    IMediaStorage storage) : ISocialShareKitService
{
    public const string Pinterest = "pinterest";
    public const string TikTok = "tiktok";

    /// <summary>Pinterest's recommended 2:3 pin.</summary>
    public const int PinWidth = 1000, PinHeight = 1500;

    /// <summary>TikTok's full-screen 9:16 frame.</summary>
    public const int TikTokWidth = 1080, TikTokHeight = 1920;

    /// <summary>Pins made from mock-ups: one each, the first approved first.</summary>
    public const int MaxImagePins = 5;

    /// <summary>Photos in a TikTok photo post.</summary>
    public const int MaxCarouselPhotos = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<Result<SocialShareKitDto>> GetAsync(Guid productId, CancellationToken cancellationToken = default) =>
        currentUser.IsAuthenticated && currentUser.UserId is Guid userId
            ? BuildAsync(productId, userId, cancellationToken)
            : Task.FromResult(Result.Failure<SocialShareKitDto>(ShopErrors.Unauthenticated()));

    public async Task<Result<SocialShareKitDto>> BuildAsync(Guid productId, Guid userId, CancellationToken cancellationToken = default)
    {
        var product = await products.Query()
            .Where(row => row.Id == productId && row.UserId == userId && row.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
            return Result.Failure<SocialShareKitDto>(ShopErrors.ProductNotFound());

        var warnings = new List<string>();
        var content = await listingContent.GetAsync(productId, cancellationToken);
        if (content.IsFailure)
            warnings.Add("No listing content was generated for this product yet; the captions use its name and description.");
        var listingUrl = await ListingUrlAsync(productId, userId, cancellationToken);
        if (listingUrl is null)
            warnings.Add("The product is not on Etsy yet: publish it first so the pins link to the listing and the captions can point to your shop.");
        var listing = new SocialListingText(
            content.IsSuccess ? content.Value.Title : product.Name,
            content.IsSuccess ? content.Value.Description : product.InputDescription ?? product.Name,
            content.IsSuccess ? content.Value.Tags.OrderBy(tag => tag.Position).Select(tag => tag.Value).ToArray() : [],
            listingUrl);

        var approvedMockups = await mockups.Query()
            .Include(row => row.MockupTemplate)
            .Where(row => row.ProductId == productId && row.DeletedAt == null && row.ApprovalStatus == "approved"
                && row.ApprovedRevision == row.MetadataRevision)
            .OrderBy(row => row.CreatedAt)
            .Take(MaxCarouselPhotos)
            .ToListAsync(cancellationToken);
        if (approvedMockups.Count == 0)
            warnings.Add("The product has no approved mock-up yet; approve some to get image pins and a TikTok photo post.");
        // A real photo of the item leads: buyers on Pinterest trust a pin that shows what ships.
        approvedMockups = approvedMockups.OrderBy(ListingPhotos.IsAiMade).ToList();

        var approvedVideos = await videos.Query()
            .Where(row => row.ProductId == productId && row.DeletedAt == null && row.ApprovalStatus == "approved" && row.StorageKey != null)
            .OrderByDescending(row => row.UpdatedAt ?? row.CreatedAt)
            .ToListAsync(cancellationToken);
        var video = approvedVideos.FirstOrDefault(row => row.AspectRatio == "9:16") ?? approvedVideos.FirstOrDefault();
        if (video is null)
            warnings.Add("The product has no approved video yet; generate one in 9:16 for a TikTok and a video pin.");
        else if (video.AspectRatio != "9:16")
            warnings.Add($"The approved video is {video.AspectRatio}; TikTok fills the screen with 9:16, so render one in 9:16 for it.");

        var slug = FileSlug(product.Name);
        var pin = SocialCaptionBuilder.Pinterest(listing, product.Name);
        var posts = new List<SocialPostDto>();
        foreach (var (mockup, index) in approvedMockups.Take(MaxImagePins).Select((row, index) => (row, index)))
            posts.Add(new SocialPostDto(Pinterest, "image", [Fitted(mockup, PinWidth, PinHeight, $"{slug}-pin-{index + 1}.jpg")],
                pin.Title, pin.Description, [], listingUrl, pin.AltText));
        if (video is not null)
            posts.Add(new SocialPostDto(Pinterest, "video", [Video(video, $"{slug}-pin-video.mp4")], pin.Title, pin.Description, [], listingUrl, pin.AltText));

        var caption = SocialCaptionBuilder.TikTok(listing);
        var hashtags = SocialCaptionBuilder.Hashtags(listing.Tags, SocialCaptionBuilder.TikTokHashtags);
        if (video is not null)
            posts.Add(new SocialPostDto(TikTok, "video", [Video(video, $"{slug}-tiktok.mp4")], null, caption, hashtags, null, null));
        if (approvedMockups.Count > 0)
            posts.Add(new SocialPostDto(TikTok, "carousel",
                approvedMockups.Select((mockup, index) => Fitted(mockup, TikTokWidth, TikTokHeight, $"{slug}-tiktok-photo-{index + 1}.jpg")).ToArray(),
                null, caption, hashtags, null, null));

        return Result.Success(new SocialShareKitDto(product.Id, product.Name, listingUrl, posts, warnings));
    }

    private SocialMediaDto Fitted(MockupImage mockup, int width, int height, string fileName) =>
        new("image", storage.SignReadFitted(mockup.StorageKey, mockup.StorageVersion, width, height),
            storage.SignReadFitted(mockup.StorageKey, mockup.StorageVersion, width, height, attachment: true), width, height, fileName);

    private SocialMediaDto Video(PromoVideo video, string fileName)
    {
        var (width, height) = video.AspectRatio switch
        {
            "9:16" => (1080, 1920),
            "16:9" => (1920, 1080),
            _ => (1080, 1080),
        };
        return new("video", storage.SignRead(video.StorageKey!, "video", video.StorageVersion),
            storage.SignRead(video.StorageKey!, "video", video.StorageVersion, attachment: true), width, height, fileName);
    }

    /// <summary>
    /// The product's listing on Etsy: pushed there directly, or published by Printify. Null while it has none, so a pin
    /// does not link to a page that does not exist.
    /// </summary>
    private async Task<string?> ListingUrlAsync(Guid productId, Guid userId, CancellationToken cancellationToken)
    {
        var direct = await etsyUploads.Query()
            .Where(row => row.ProductId == productId && row.EtsyIntegration.UserId == userId && row.UploadStatus == "success" && row.EtsyListingId != null)
            .OrderByDescending(row => row.AttemptedAt)
            .Select(row => row.EtsyListingId)
            .FirstOrDefaultAsync(cancellationToken);
        if (direct is not null)
            return EtsyListingUrl(direct);

        var printify = await printifyUploads.Query()
            .Where(row => row.ProductId == productId && row.PrintifyIntegration.UserId == userId && row.UploadStatus == "success" && row.ApiResponse != null)
            .OrderByDescending(row => row.AttemptedAt)
            .Select(row => row.ApiResponse)
            .ToListAsync(cancellationToken);
        foreach (var response in printify)
        {
            try
            {
                if (JsonSerializer.Deserialize<PrintifyListingState>(response!, Json)?.EtsyListingId is { Length: > 0 } listingId)
                    return EtsyListingUrl(listingId);
            }
            catch (JsonException)
            {
                // A log written before the listing existed: look at the next one.
            }
        }
        return null;
    }

    private static string EtsyListingUrl(string listingId) => $"https://www.etsy.com/listing/{listingId}";

    /// <summary>A file name made of the product's name: lower case words joined by dashes.</summary>
    private static string FileSlug(string name)
    {
        var words = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var slug = string.Join('-', words);
        return slug.Length == 0 ? "product" : slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }

    /// <summary>The part of a Printify log that names the Etsy listing Printify published.</summary>
    private sealed record PrintifyListingState(string? EtsyListingId);
}
