using System.Net;
using System.Text.Json;
using APCS.Application.Abstractions.Commerce;
using APCS.Infrastructure.Services;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class PrintifyClientTests
{
    [TestMethod]
    public void Reason_PrefersTheSpecificReasonOverTheGenericMessage() =>
        PrintifyClient.Reason("""{"status":"error","code":8203,"message":"Validation failed.","errors":{"reason":"Image has low quality","code":8203}}""")
            .Should().Be("Image has low quality");

    [TestMethod]
    public void Reason_FieldErrors_GiveTheFirstMessage() =>
        PrintifyClient.Reason("""{"message":"Validation failed.","errors":{"variants":["The variants must not be empty."]}}""")
            .Should().Be("The variants must not be empty.");

    [TestMethod]
    public void Reason_NotJson_IsNull() => PrintifyClient.Reason("<html>").Should().BeNull();

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized, PrintifyFailureKind.Unauthorized)]
    [DataRow(HttpStatusCode.TooManyRequests, PrintifyFailureKind.RateLimited)]
    [DataRow(HttpStatusCode.UnprocessableEntity, PrintifyFailureKind.Rejected)]
    [DataRow(HttpStatusCode.InternalServerError, PrintifyFailureKind.Unavailable)]
    public void Classify_ByStatus(HttpStatusCode status, PrintifyFailureKind expected) =>
        PrintifyClient.Classify(status).Should().Be(expected);

    [TestMethod]
    public void ReadProduct_ReadsCostsAndTheEtsyListing()
    {
        using var document = JsonDocument.Parse("""
            {
              "id": "5d39b411749d0a000f30e0f4", "title": "Cat Tee", "is_locked": false,
              "external": { "id": "4455667788", "handle": "https://www.etsy.com/listing/4455667788" },
              "variants": [
                { "id": 101, "title": "White / S", "cost": 1010, "price": 2499, "is_enabled": true, "is_available": true },
                { "id": 103, "title": "Black / S", "cost": 1159, "price": 2499, "is_enabled": false, "is_available": false }
              ],
              "images": [ { "src": "https://images.printify.com/mockup.jpg", "variant_ids": [101], "position": "front", "is_default": true } ]
            }
            """);

        var product = PrintifyClient.ReadProduct(document.RootElement);

        product.ExternalId.Should().Be("4455667788");
        product.IsLocked.Should().BeFalse();
        product.Variants.Should().HaveCount(2);
        product.Variants[0].CostCents.Should().Be(1010);
        product.Variants[1].IsEnabled.Should().BeFalse();
        product.Variants[1].IsAvailable.Should().BeFalse();
        product.ImageUrls.Should().ContainSingle();
    }

    [TestMethod]
    public void ReadProduct_NotYetPublished_HasNoListing()
    {
        using var document = JsonDocument.Parse("""{ "id": "p", "title": "t", "is_locked": true, "external": null, "variants": [] }""");

        var product = PrintifyClient.ReadProduct(document.RootElement);

        product.ExternalId.Should().BeNull();
        product.IsLocked.Should().BeTrue();
    }
}
