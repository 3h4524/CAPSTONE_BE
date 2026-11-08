using System.Net;
using System.Text;
using APCS.Application.Abstractions.Commerce;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class EtsyShopClientHttpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> bodies = [];
    private Func<HttpRequestMessage, HttpResponseMessage> respond = _ => Json("{}");

    private EtsyShopClient Create()
    {
        var handler = new Handler(async request =>
        {
            requests.Add(request);
            bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync());
            return respond(request);
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns((string name) => new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = name == EtsyShopClient.MediaDownloadClientName ? null : new Uri("https://api.etsy.com/v3/application/"),
        });
        return new EtsyShopClient(factory.Object,
            Microsoft.Extensions.Options.Options.Create(new EtsyOptions { ApiKey = "key", SharedSecret = "secret" }),
            Microsoft.Extensions.Options.Options.Create(new AppOptions { BaseUrl = "http://localhost:3000/" }),
            new EphemeralDataProtectionProvider(), new FakeTimeProvider(Now), NullLogger<EtsyShopClient>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private string Path(int index = 0) => requests[index].RequestUri!.PathAndQuery;

    [TestMethod]
    public void BuildAuthorizationUrl_CarriesTheAppTheScopesAndThePkceChallenge()
    {
        var url = Create().BuildAuthorizationUrl("state-1", "challenge-1");

        url.Should().StartWith("https://www.etsy.com/oauth/connect?")
            .And.Contain("client_id=key").And.Contain("state=state-1").And.Contain("code_challenge=challenge-1")
            .And.Contain("code_challenge_method=S256").And.Contain("scope=shops_r%20listings_r");
    }

    [TestMethod]
    public void BuildReturnUrl_PointsAtTheClientCallbackPage()
    {
        var client = Create();

        client.BuildReturnUrl("no_shop").Should().Be("http://localhost:3000/etsy/callback?etsy=no_shop");
        client.IsConfigured.Should().BeTrue();
    }

    [TestMethod]
    public async Task ExchangeCodeAsync_ReadsBothTokensAndWhenTheAccessTokenExpires()
    {
        respond = _ => Json("""{"access_token":"a1","refresh_token":"r1","expires_in":3600}""");

        var tokens = await Create().ExchangeCodeAsync("code", "verifier", default);

        tokens.AccessToken.Should().Be("a1");
        tokens.RefreshToken.Should().Be("r1");
        tokens.AccessTokenExpiresAtUtc.Should().Be(Now.AddSeconds(3600));
        requests[0].RequestUri!.AbsoluteUri.Should().Be("https://api.etsy.com/v3/public/oauth/token");
        bodies[0].Should().Contain("grant_type=authorization_code").And.Contain("code_verifier=verifier");
    }

    [TestMethod]
    public async Task RefreshAsync_AnExpiredGrant_MeansReconnect_WithoutEchoingTheBody()
    {
        respond = _ => Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);

        var act = () => Create().RefreshAsync("old", default);

        var thrown = await act.Should().ThrowAsync<EtsyApiException>();
        thrown.Which.Kind.Should().Be(EtsyFailureKind.Unauthorized);
        thrown.Which.Reason.Should().BeNull();
    }

    [TestMethod]
    public async Task RefreshAsync_NoTokenInTheAnswer_IsUnavailable()
    {
        respond = _ => Json("""{"token_type":"Bearer"}""");

        var act = () => Create().RefreshAsync("old", default);

        (await act.Should().ThrowAsync<EtsyApiException>()).Which.Kind.Should().Be(EtsyFailureKind.Unavailable);
    }

    [TestMethod]
    public async Task GetOwnShopIdAsync_ReadsTheShop_AndAnAccountWithoutAShopIsNull()
    {
        respond = _ => Json("""{"user_id":1,"shop_id":12345}""");
        (await Create().GetOwnShopIdAsync("t", default)).Should().Be("12345");
        requests[0].Headers.Authorization!.Parameter.Should().Be("t");

        respond = _ => Json("""{"error":"Could not find a shop for User"}""", HttpStatusCode.NotFound);
        (await Create().GetOwnShopIdAsync("t", default)).Should().BeNull();
    }

    [TestMethod]
    public async Task GetShopAsync_ReadsTheShopRecord()
    {
        respond = _ => Json("""
            {"shop_id":12345,"shop_name":"APCSCo","title":"Tees","url":"https://etsy.com/shop/APCSCo","icon_url_fullxfull":"https://i/icon.png",
             "currency_code":"USD","listing_active_count":3,"digital_listing_count":1,"num_favorers":9,"transaction_sold_count":4,
             "review_average":4.857,"review_count":7,"is_vacation":false,"create_date":1700000000,"image_url_760x100":"https://i/cover.png",
             "announcement":"Hello","is_using_structured_policies":false,"policy_refunds":"30 days","is_etsy_payments_onboarded":true,
             "accepts_custom_requests":true,"shipping_from_country_iso":"US"}
            """);

        var shop = await Create().GetShopAsync("t", "12345", default);

        shop.ShopName.Should().Be("APCSCo");
        shop.CurrencyCode.Should().Be("USD");
        shop.ActiveListingCount.Should().Be(3);
        shop.ReviewAverage.Should().Be(4.86m);
        shop.OpenedAtUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000000));
        shop.Announcement.Should().Be("Hello");
        shop.HasReturnPolicy.Should().BeTrue();
        shop.ShipsFromCountry.Should().Be("US");
        Path().Should().Be("/v3/application/shops/12345");
    }

    [TestMethod]
    public async Task GetListingsAsync_ReadsPriceTagsImagesAndDates()
    {
        respond = _ => Json("""
            {"count":42,"results":[{"listing_id":7,"title":"Cat Tee","url":"https://etsy.com/listing/7","state":"active",
              "price":{"amount":2499,"divisor":100,"currency_code":"USD"},"quantity":999,"views":10,"num_favorers":2,
              "tags":["cat","tee",5],"images":[{"url_570xN":"https://i/1.jpg"},{"url_570xN":"https://i/2.jpg"}],
              "created_timestamp":1700000000,"updated_timestamp":1700000100,"ending_timestamp":1710000000}]}
            """);

        var page = await Create().GetListingsAsync("t", "12345", "active", 500, withImages: true, default);

        page.Count.Should().Be(42);
        var listing = page.Listings.Should().ContainSingle().Subject;
        listing.Price!.Amount.Should().Be(24.99m);
        listing.Tags.Should().Equal("cat", "tee");
        listing.ImageCount.Should().Be(2);
        listing.ImageUrl.Should().Be("https://i/1.jpg");
        Path().Should().Contain("limit=100").And.Contain("includes=Images");
    }

    [TestMethod]
    public async Task GetReceiptsAsync_PagesUntilAShortPage_AndReadsItemsAndRefunds()
    {
        var page = 0;
        respond = _ =>
        {
            page++;
            var receipts = Enumerable.Range(0, page == 1 ? 100 : 1).Select(i => $$$"""
                {"receipt_id":{{{page * 1000 + i}}},"status":"paid","is_paid":true,"is_shipped":false,"created_timestamp":1700000000,"country_iso":"US",
                 "grandtotal":{"amount":3000,"divisor":100,"currency_code":"USD"},
                 "refunds":[{"amount":{"amount":500,"divisor":100,"currency_code":"USD"}},{"amount":{"amount":100,"divisor":100,"currency_code":"USD"}}],
                 "transactions":[{"listing_id":7,"title":"Cat Tee","quantity":2,"price":{"amount":1500,"divisor":100,"currency_code":"USD"},
                   "paid_timestamp":1700000000,"expected_ship_date":1700500000}]}
                """);
            return Json($$"""{"count":101,"results":[{{string.Join(',', receipts)}}]}""");
        };

        var receipts = await Create().GetReceiptsAsync("t", "12345", Now.AddDays(-30), Now, 500, default);

        receipts.Should().HaveCount(101);
        requests.Should().HaveCount(2);
        Path(1).Should().Contain("offset=100");
        var first = receipts[0];
        first.Refunded!.Amount.Should().Be(6m);
        first.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        first.CountryIso.Should().Be("US");
    }

    [TestMethod]
    public async Task GetReceiptPaymentAsync_ReadsFees_AndNoPaymentIsNull()
    {
        respond = _ => Json("""{"results":[{"amount_gross":{"amount":3000,"divisor":100,"currency_code":"USD"},"amount_fees":{"amount":290,"divisor":100,"currency_code":"USD"},"amount_net":{"amount":2710,"divisor":100,"currency_code":"USD"}}]}""");
        (await Create().GetReceiptPaymentAsync("t", "1", "2", default))!.Fees!.Amount.Should().Be(2.90m);

        respond = _ => Json("""{"results":[]}""");
        (await Create().GetReceiptPaymentAsync("t", "1", "2", default)).Should().BeNull();
    }

    [TestMethod]
    public async Task GetReviewsAsync_KeepsOnlyRatingsFromOneToFive()
    {
        respond = _ => Json("""{"results":[{"rating":5,"review":"Love it","listing_id":7,"created_timestamp":1700000000},{"rating":0},{"rating":3,"review":""}]}""");

        var reviews = await Create().GetReviewsAsync("t", "12345", 25, default);

        reviews.Should().HaveCount(2);
        reviews[0].Text.Should().Be("Love it");
        reviews[1].Text.Should().BeNull();
    }

    [TestMethod]
    public async Task GetShopSetupAsync_CountsProfilesPartnersAndSections()
    {
        respond = request => request.RequestUri!.AbsolutePath.EndsWith("sections")
            ? Json("""{"results":[{},{},{}]}""")
            : Json("""{"count":2,"results":[]}""");

        var setup = await Create().GetShopSetupAsync("t", "12345", default);

        setup.ShippingProfileCount.Should().Be(2);
        setup.ProductionPartnerCount.Should().Be(2);
        setup.SectionCount.Should().Be(3);
    }

    [TestMethod]
    public async Task ShopOptions_ReadProfilesProcessingAndPartners()
    {
        var client = Create();
        respond = _ => Json("""{"results":[{"shipping_profile_id":5,"title":"US - POD"},{"shipping_profile_id":6,"title":"Old","is_deleted":true},{"title":"No id"}]}""");
        (await client.GetShippingProfilesAsync("t", "1", default)).Should().ContainSingle().Which.Should().Be(new EtsyOption(5, "US - POD"));

        respond = _ => Json("""{"results":[{"readiness_state_id":11,"readiness_state":"made_to_order","min_processing_days":3,"max_processing_days":5},{"id":12,"readiness_state":"ready_to_ship","processing_days_display_label":"1 day"}]}""");
        (await client.GetProcessingProfilesAsync("t", "1", default)).Select(option => option.Label)
            .Should().Equal("Made to order · 3–5 days", "Ready to ship · 1 day");

        respond = _ => Json("""{"results":[{"production_partner_id":21,"partner_name":"Printify","location":"US"},{"partner_name":"No id"}]}""");
        (await client.GetProductionPartnersAsync("t", "1", default)).Should().ContainSingle().Which.Label.Should().Be("Printify · US");
    }

    [TestMethod]
    public async Task GetSellerTaxonomyAsync_FlattensTheTreeIntoPaths()
    {
        respond = _ => Json("""{"results":[{"id":1,"name":"Clothing","children":[{"id":2,"name":"Shirts","children":[{"id":3,"name":"T-shirts"}]}]}]}""");

        var nodes = await Create().GetSellerTaxonomyAsync(default);

        nodes.Select(node => node.Path).Should().Equal("Clothing", "Clothing > Shirts", "Clothing > Shirts > T-shirts");
        requests[0].Headers.Authorization.Should().BeNull();
    }

    [TestMethod]
    public async Task CreateDraftListingAsync_SendsEveryFieldAsAForm()
    {
        respond = _ => Json("""{"listing_id":987,"url":"https://etsy.com/listing/987"}""");

        var created = await Create().CreateDraftListingAsync("t", "12345",
            new EtsyDraftListing("Cat Tee", "Soft.", 24.99m, 999, 482, "physical", "i_did", "made_to_order", ["cat", "tee"], 5, 11, [21, 22]), default);

        created.Should().Be(new EtsyCreatedListing("987", "https://etsy.com/listing/987"));
        bodies[0].Should().Contain("price=24.99").And.Contain("tags=cat%2Ctee").And.Contain("shipping_profile_id=5")
            .And.Contain("readiness_state_id=11").And.Contain("production_partner_ids=21%2C22").And.Contain("is_supply=false");
    }

    [TestMethod]
    public async Task CreateDraftListingAsync_EtsyRefuses_CarriesItsReason()
    {
        respond = _ => Json("""{"error":"Price must be at least 0.20 USD"}""", HttpStatusCode.BadRequest);

        var act = () => Create().CreateDraftListingAsync("t", "1",
            new EtsyDraftListing("T", "D", 0.1m, 1, 1, "download", "i_did", "made_to_order", [], null, null, []), default);

        var thrown = await act.Should().ThrowAsync<EtsyApiException>();
        thrown.Which.Kind.Should().Be(EtsyFailureKind.Unavailable);
        thrown.Which.Reason.Should().Be("Price must be at least 0.20 USD");
    }

    [TestMethod]
    public async Task CreateDraftListingAsync_NoListingIdInTheAnswer_IsUnavailable()
    {
        respond = _ => Json("{}");

        var act = () => Create().CreateDraftListingAsync("t", "1",
            new EtsyDraftListing("T", "D", 5, 1, 1, "download", "i_did", "made_to_order", [], null, null, []), default);

        await act.Should().ThrowAsync<EtsyApiException>();
    }

    [TestMethod]
    public async Task Uploads_SendImageVideoAndFileAsMultipart()
    {
        respond = request => request.RequestUri!.Host == "cdn.test"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }
            : Json("{}");
        var client = Create();

        await client.UploadListingImageAsync("t", "1", "987", [1, 2], "mockup-1.png", 1, "Cat Tee", default);
        await client.UploadListingVideoAsync("t", "1", "987", "https://cdn.test/video.mp4", "Cat Tee", default);
        await client.UploadListingFileAsync("t", "1", "987", "https://cdn.test/design.jpg", "design-1.jpg", 1, default);

        requests.Select(request => request.RequestUri!.AbsolutePath).Should().Equal(
            "/v3/application/shops/1/listings/987/images", "/video.mp4", "/v3/application/shops/1/listings/987/videos",
            "/design.jpg", "/v3/application/shops/1/listings/987/files");
        bodies[0].Should().Contain("alt_text").And.Contain("Cat Tee");
        bodies[4].Should().Contain("design-1.jpg").And.Contain("image/jpeg");
    }

    [TestMethod]
    public async Task Uploads_TheStoredMediaCannotBeRead_IsUnavailable()
    {
        respond = request => request.RequestUri!.Host == "cdn.test" ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json("{}");
        var client = Create();

        var video = () => client.UploadListingVideoAsync("t", "1", "987", "https://cdn.test/video.mp4", "n", default);
        var file = () => client.UploadListingFileAsync("t", "1", "987", "https://cdn.test/d.png", "d.png", 1, default);

        (await video.Should().ThrowAsync<EtsyApiException>()).Which.Kind.Should().Be(EtsyFailureKind.Unavailable);
        (await file.Should().ThrowAsync<EtsyApiException>()).Which.Kind.Should().Be(EtsyFailureKind.Unavailable);
    }

    [TestMethod]
    public async Task Send_RateLimitedNetworkDownTimeoutAndUnreadable_AreClassified()
    {
        respond = _ => Json("{}", HttpStatusCode.TooManyRequests);
        (await ((Func<Task>)(() => Create().GetShopAsync("t", "1", default))).Should().ThrowAsync<EtsyApiException>()).Which.Kind.Should().Be(EtsyFailureKind.RateLimited);

        respond = _ => throw new HttpRequestException("down");
        (await ((Func<Task>)(() => Create().GetShopAsync("t", "1", default))).Should().ThrowAsync<EtsyApiException>()).Which.Message.Should().Contain("reached");

        respond = _ => throw new TaskCanceledException("slow");
        (await ((Func<Task>)(() => Create().GetShopAsync("t", "1", default))).Should().ThrowAsync<EtsyApiException>()).Which.Message.Should().Contain("time");

        respond = _ => Json("<html>");
        (await ((Func<Task>)(() => Create().GetShopAsync("t", "1", default))).Should().ThrowAsync<EtsyApiException>()).Which.Message.Should().Contain("unreadable");

        respond = _ => Json("not json", HttpStatusCode.Forbidden);
        (await ((Func<Task>)(() => Create().GetShopAsync("t", "1", default))).Should().ThrowAsync<EtsyApiException>()).Which.Kind.Should().Be(EtsyFailureKind.Unauthorized);
    }

    [TestMethod]
    public void ProtectThenUnprotect_ReturnsTheValue()
    {
        var client = Create();

        client.Unprotect(client.Protect("secret-token")).Should().Be("secret-token");
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
