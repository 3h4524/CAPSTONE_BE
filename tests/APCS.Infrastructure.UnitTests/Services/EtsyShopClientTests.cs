using System.Net;
using APCS.Application.Abstractions.Commerce;
using APCS.Infrastructure.Services;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class EtsyShopClientTests
{
    [TestMethod]
    public void Classify_InvalidGrantOnTheTokenEndpoint_MeansReconnect() =>
        EtsyShopClient.Classify(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"expired"}""")
            .Should().Be(EtsyFailureKind.Unauthorized);

    [TestMethod]
    public void Classify_OtherBadRequest_IsNotBlamedOnTheSeller() =>
        EtsyShopClient.Classify(HttpStatusCode.BadRequest, """{"error":"invalid_request"}""")
            .Should().Be(EtsyFailureKind.Unavailable);

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized, EtsyFailureKind.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden, EtsyFailureKind.Unauthorized)]
    [DataRow(HttpStatusCode.TooManyRequests, EtsyFailureKind.RateLimited)]
    [DataRow(HttpStatusCode.BadGateway, EtsyFailureKind.Unavailable)]
    public void Classify_ByStatus(HttpStatusCode status, EtsyFailureKind expected) =>
        EtsyShopClient.Classify(status, "not json").Should().Be(expected);
}
