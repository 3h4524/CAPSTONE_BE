using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Application.Features.Social;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Social;

[TestClass]
public sealed class SocialShareKitServiceTests
{
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid productId = Guid.NewGuid();
    private readonly List<MockupImage> mockupRows = [];
    private readonly List<PromoVideo> videoRows = [];
    private readonly List<EtsyUploadLog> etsyRows = [];
    private readonly List<PrintifyUploadLog> printifyRows = [];
    private readonly Mock<IListingContentService> listing = new();
    private readonly Mock<IMediaStorage> storage = new();

    private SocialShareKitService Create()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(userId);
        var products = Repository(new[] { new Product { Id = productId, UserId = userId, Name = "Math Teacher Tee", InputDescription = "A tee." } }.ToList());
        storage.Setup(x => x.SignReadFitted(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Returns((string key, string? _, int width, int height, bool download) => $"https://cdn.test/{key}/{width}x{height}{(download ? "?dl" : "")}");
        storage.Setup(x => x.SignRead(It.IsAny<string>(), "video", It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns((string key, string _, string? _, bool download) => $"https://cdn.test/{key}.mp4{(download ? "?dl" : "")}");
        return new SocialShareKitService(user.Object, products.Object, Repository(mockupRows).Object, Repository(videoRows).Object,
            Repository(etsyRows).Object, Repository(printifyRows).Object, listing.Object, storage.Object);
    }

    private static Mock<IRepository<T>> Repository<T>(List<T> rows) where T : class
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        return repository;
    }

    private void Listed() => listing.Setup(x => x.GetAsync(productId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new ListingContentResponseDto(
        productId, Guid.NewGuid(), "Funny Math Teacher Shirt, Pi Day Shirt", "A soft tee for teachers.",
        [new ListingContentTagDto("math pun shirt", null, 1), new ListingContentTagDto("pi day shirt", null, 2)], 1, "model", null)));

    private void Mockup(string key, int minute) => mockupRows.Add(new MockupImage
    {
        Id = Guid.NewGuid(), ProductId = productId, StorageKey = key, StorageVersion = "1", ApprovalStatus = "approved", ApprovedRevision = 1,
        MetadataRevision = 1, CreatedAt = new DateTime(2026, 11, 1, 10, minute, 0),
    });

    private void Video(string aspect) => videoRows.Add(new PromoVideo
    {
        Id = Guid.NewGuid(), ProductId = productId, StorageKey = $"video-{aspect.Replace(':', 'x')}", AspectRatio = aspect, ApprovalStatus = "approved",
        FileFormat = "mp4", PlatformTarget = "etsy", Status = "completed", CreatedAt = new DateTime(2026, 11, 1),
    });

    [TestMethod]
    public async Task GetAsync_BuildsPinsAndTikTokPosts_SizedForEachPlatform_LinkedToTheEtsyListing()
    {
        Listed();
        Mockup("m1", 1);
        Mockup("m2", 2);
        Video("9:16");
        etsyRows.Add(new EtsyUploadLog
        {
            ProductId = productId, UploadStatus = "success", EtsyListingId = "123", AttemptedAt = DateTime.UtcNow,
            EtsyIntegration = new EtsyIntegration { UserId = userId },
        });

        var kit = (await Create().GetAsync(productId)).Value;

        kit.ListingUrl.Should().Be("https://www.etsy.com/listing/123");
        kit.Warnings.Should().BeEmpty();
        var pins = kit.Posts.Where(post => post.Platform == "pinterest").ToArray();
        pins.Select(post => post.Kind).Should().Equal("image", "image", "video");
        pins[0].Media.Single().Should().BeEquivalentTo(new SocialMediaDto("image", "https://cdn.test/m1/1000x1500", "https://cdn.test/m1/1000x1500?dl", 1000, 1500, "math-teacher-tee-pin-1.jpg"));
        pins.Should().OnlyContain(post => post.Link == "https://www.etsy.com/listing/123" && post.Title == "Funny Math Teacher Shirt, Pi Day Shirt");
        var tiktok = kit.Posts.Where(post => post.Platform == "tiktok").ToArray();
        tiktok.Select(post => post.Kind).Should().Equal("video", "carousel");
        tiktok[1].Media.Should().HaveCount(2).And.OnlyContain(media => media.Width == 1080 && media.Height == 1920);
        tiktok[0].Hashtags.Should().StartWith(["#mathpunshirt", "#pidayshirt"]);
    }

    [TestMethod]
    public async Task GetAsync_AListingPrintifyPublished_IsLinked()
    {
        Listed();
        Mockup("m1", 1);
        printifyRows.Add(new PrintifyUploadLog
        {
            ProductId = productId, UploadStatus = "success", AttemptedAt = DateTime.UtcNow, ApiResponse = """{"stage":"published","etsyListingId":"987"}""",
            PrintifyIntegration = new PrintifyIntegration { UserId = userId },
        });

        (await Create().GetAsync(productId)).Value.ListingUrl.Should().Be("https://www.etsy.com/listing/987");
    }

    [TestMethod]
    public async Task GetAsync_NothingApprovedYet_SaysWhatToDoFirst()
    {
        listing.Setup(x => x.GetAsync(productId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure<ListingContentResponseDto>(Error.NotFound("x", "none")));
        Video("1:1");

        var kit = (await Create().GetAsync(productId)).Value;

        kit.ListingUrl.Should().BeNull();
        kit.Posts.Should().OnlyContain(post => post.Kind == "video" && post.Link == null);
        kit.Warnings.Should().HaveCount(4).And.Contain(warning => warning.Contains("TikTok fills the screen with 9:16"));
    }

    [TestMethod]
    public async Task GetAsync_SomeoneElsesProduct_IsNotFound()
    {
        var result = await Create().GetAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("shop.product_not_found");
    }
}
