using System.Net;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Commerce;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class PrintifyClientHttpTests
{
    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> bodies = [];
    private Func<HttpRequestMessage, HttpResponseMessage> respond = _ => Json("{}");

    private PrintifyClient Create()
    {
        var handler = new Handler(async request =>
        {
            requests.Add(request);
            bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync());
            return respond(request);
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(PrintifyClient.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.printify.com/v1/") });
        return new PrintifyClient(factory.Object, NullLogger<PrintifyClient>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private string Path(int index = 0) => requests[index].RequestUri!.AbsolutePath;

    private const string ProductJson = """
        {"id":"p-1","title":"Cat Tee","is_locked":false,
         "variants":[{"id":101,"title":"White / S","cost":1010,"price":2499,"is_enabled":true,"is_available":true}],
         "images":[{"src":"https://images.printify.com/1.jpg"}]}
        """;

    [TestMethod]
    public async Task GetShopsAsync_ReadsStoresAndTheirChannel_WithTheSellersToken()
    {
        respond = _ => Json("""[{"id":5,"title":"Old","sales_channel":"disconnected"},{"id":777,"title":"APCSCo","sales_channel":"etsy"},{"title":"No id"}]""");

        var shops = await Create().GetShopsAsync("token", default);

        shops.Should().Equal(new PrintifyShop(5, "Old", "disconnected"), new PrintifyShop(777, "APCSCo", "etsy"));
        requests[0].Headers.Authorization!.Parameter.Should().Be("token");
        Path().Should().Be("/v1/shops.json");
    }

    [TestMethod]
    public async Task Catalog_ReadsBlueprintsProvidersVariantsAndShipping()
    {
        var client = Create();

        respond = _ => Json("""[{"id":6,"title":"Unisex Softstyle T-Shirt","brand":"Gildan","model":"64000","images":["https://img/6.png"]}]""");
        (await client.GetBlueprintsAsync("t", default)).Should().ContainSingle().Which.ImageUrl.Should().Be("https://img/6.png");

        respond = _ => Json("""{"data":[{"id":99,"title":"Monster Digital","location":{"country":"US","region":"CA"}},{"id":2,"title":"No location"}]}""");
        var providers = await client.GetProvidersAsync("t", default);
        providers[0].Should().Be(new PrintifyProvider(99, "Monster Digital", "US", "CA"));
        providers[1].Country.Should().BeNull();

        respond = _ => Json("""[{"id":99,"title":"Monster Digital"}]""");
        (await client.GetBlueprintProvidersAsync("t", 6, default)).Should().ContainSingle().Which.Id.Should().Be(99);
        Path(2).Should().Be("/v1/catalog/blueprints/6/print_providers.json");

        respond = _ => Json("""{"id":99,"variants":[{"id":101,"title":"White / S","options":{"color":"White","size":"S"},"placeholders":[{"position":"front","width":4500,"height":5100}]},{"id":102,"title":"Bare"}]}""");
        var variants = await client.GetVariantsAsync("t", 6, 99, default);
        variants[0].Color.Should().Be("White");
        variants[0].Placeholders.Should().ContainSingle().Which.Width.Should().Be(4500);
        variants[1].Placeholders.Should().BeEmpty();

        respond = _ => Json("""{"handling_time":{"value":2,"unit":"day"},"profiles":[{"variant_ids":[101],"first_item":{"cost":475,"currency":"USD"},"additional_items":{"cost":240,"currency":"USD"},"countries":["US"]}]}""");
        var shipping = await client.GetShippingAsync("t", 6, 99, default);
        shipping.Should().ContainSingle().Which.Should().Match<PrintifyShippingRate>(rate => rate.FirstItemCents == 475 && rate.AdditionalItemCents == 240 && rate.Countries.Contains("US"));
    }

    [TestMethod]
    public async Task GetShippingAsync_NoProfiles_IsEmpty()
    {
        respond = _ => Json("""{"handling_time":null}""");

        (await Create().GetShippingAsync("t", 6, 99, default)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task UploadImageAsync_HandsPrintifyTheUrl()
    {
        respond = _ => Json("""{"id":"img-1","width":4500,"height":5400}""");

        var image = await Create().UploadImageAsync("t", "design-1.png", "https://cdn.test/d.png", default);

        image.Should().Be(new PrintifyUploadedImage("img-1", 4500, 5400));
        bodies[0].Should().Contain("\"url\":\"https://cdn.test/d.png\"").And.Contain("\"file_name\":\"design-1.png\"");
    }

    [TestMethod]
    public async Task UploadImageAsync_NoIdInTheAnswer_IsUnavailable()
    {
        respond = _ => Json("{}");

        var act = () => Create().UploadImageAsync("t", "d.png", "https://cdn.test/d.png", default);

        (await act.Should().ThrowAsync<PrintifyApiException>()).Which.Kind.Should().Be(PrintifyFailureKind.Unavailable);
    }

    [TestMethod]
    public async Task CreateProductAsync_PrintsTheDesignCenteredOnEveryVariant()
    {
        respond = _ => Json(ProductJson);

        var product = await Create().CreateProductAsync("t", 777, new PrintifyNewProduct("Cat Tee", "Soft.", ["cat"], 6, 99,
            [new PrintifyVariantPrice(101, 2499, true), new PrintifyVariantPrice(102, 2499, false)], "img-1", "front", 1), default);

        product.Id.Should().Be("p-1");
        Path().Should().Be("/v1/shops/777/products.json");
        using var body = JsonDocument.Parse(bodies[0]);
        var root = body.RootElement;
        root.GetProperty("blueprint_id").GetInt64().Should().Be(6);
        root.GetProperty("variants").GetArrayLength().Should().Be(2);
        var area = root.GetProperty("print_areas")[0];
        area.GetProperty("variant_ids").GetArrayLength().Should().Be(2);
        var image = area.GetProperty("placeholders")[0].GetProperty("images")[0];
        image.GetProperty("id").GetString().Should().Be("img-1");
        image.GetProperty("x").GetDouble().Should().Be(0.5);
    }

    [TestMethod]
    public async Task ProductCalls_ReadUpdateAndPublish()
    {
        respond = _ => Json(ProductJson);
        var client = Create();

        (await client.GetProductAsync("t", 777, "p-1", default)).Variants.Should().ContainSingle();
        (await client.UpdatePricesAsync("t", 777, "p-1", [new PrintifyVariantPrice(101, 2699, true)], default)).Title.Should().Be("Cat Tee");
        respond = _ => Json("");
        await client.PublishAsync("t", 777, "p-1", default);

        requests.Select(request => $"{request.Method} {request.RequestUri!.AbsolutePath}").Should().Equal(
            "GET /v1/shops/777/products/p-1.json", "PUT /v1/shops/777/products/p-1.json", "POST /v1/shops/777/products/p-1/publish.json");
        bodies[1].Should().Contain("\"price\":2699");
        bodies[2].Should().Contain("\"shipping_template\":true");
    }

    [TestMethod]
    public async Task Send_RefusalsCarryPrintifysReason_AndOtherFailuresAreClassified()
    {
        respond = _ => Json("""{"status":"error","message":"Validation failed.","errors":{"reason":"Image has low quality"}}""", HttpStatusCode.BadRequest);
        var refused = await ((Func<Task>)(() => Create().GetShopsAsync("t", default))).Should().ThrowAsync<PrintifyApiException>();
        refused.Which.Kind.Should().Be(PrintifyFailureKind.Rejected);
        refused.Which.Reason.Should().Be("Image has low quality");

        respond = _ => Json("{}", HttpStatusCode.Unauthorized);
        (await ((Func<Task>)(() => Create().GetShopsAsync("t", default))).Should().ThrowAsync<PrintifyApiException>()).Which.Kind.Should().Be(PrintifyFailureKind.Unauthorized);

        respond = _ => throw new HttpRequestException("down");
        (await ((Func<Task>)(() => Create().GetShopsAsync("t", default))).Should().ThrowAsync<PrintifyApiException>()).Which.Message.Should().Contain("reached");

        respond = _ => throw new TaskCanceledException("slow");
        (await ((Func<Task>)(() => Create().GetShopsAsync("t", default))).Should().ThrowAsync<PrintifyApiException>()).Which.Message.Should().Contain("time");

        respond = _ => Json("<html>");
        (await ((Func<Task>)(() => Create().GetShopsAsync("t", default))).Should().ThrowAsync<PrintifyApiException>()).Which.Message.Should().Contain("unreadable");
    }

    [TestMethod]
    public void Reason_ReadsArraysStringsAndPlainMessages()
    {
        PrintifyClient.Reason("""{"errors":[{"message":"Variant 5 is disabled"}]}""").Should().Be("Variant 5 is disabled");
        PrintifyClient.Reason("""{"errors":"Shop is disconnected"}""").Should().Be("Shop is disconnected");
        PrintifyClient.Reason("""{"error":"Not found"}""").Should().Be("Not found");
        PrintifyClient.Reason("""["a"]""").Should().BeNull();
        PrintifyClient.Reason("{\"message\":\"" + new string('x', 400) + "\"}").Should().HaveLength(300);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
