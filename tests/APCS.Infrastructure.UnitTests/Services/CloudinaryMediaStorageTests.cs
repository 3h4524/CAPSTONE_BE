using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using CloudinaryDotNet;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class CloudinaryMediaStorageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
    private const string Key = "workflow-media/owner/run/job/lease/video";

    [TestMethod]
    [DataRow("image", false, "png")]
    [DataRow("image", true, "png")]
    [DataRow("video", false, "mp4")]
    [DataRow("video", true, "mp4")]
    [DataRow("raw", false, null)]
    [DataRow("raw", true, null)]
    public void SignRead_PrivateResource_SignsCorrectPathFormatAttachmentAndTenMinuteExpiry(string type, bool attachment, string? format)
    {
        var fixture = Create();
        var url = fixture.Service.SignRead(Key, type, "synthetic-version", attachment);
        var uri = new Uri(url);
        var query = QueryHelpers.ParseQuery(uri.Query);
        uri.Scheme.Should().Be("https");
        uri.Host.Should().Be("api.cloudinary.com");
        uri.AbsolutePath.Should().Be($"/v1_1/unit-test-cloud/{type}/download");
        query["public_id"].ToString().Should().Be("apcs/private/" + Key);
        query["type"].ToString().Should().Be("authenticated");
        query["attachment"].ToString().Should().Be(attachment ? "true" : "false");
        query["expires_at"].ToString().Should().Be(Now.AddMinutes(10).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (format == null) query.Should().NotContainKey("format");
        else query["format"].ToString().Should().Be(format);
        if (type == "video") query["target_filename"].ToString().Should().Be("video.mp4");
        if (type == "raw") query["target_filename"].ToString().Should().Be("video-package.zip");
        url.Should().NotContain(fixture.Options.ApiSecret);
        var signedParameters = query.Where(x => x.Key is not ("signature" or "api_key"))
            .ToDictionary(x => x.Key, x => (object)x.Value.ToString(), StringComparer.Ordinal);
        query["signature"].ToString().Should().Be(Signer(fixture.Options).Api.SignParameters(signedParameters));
        fixture.Clients.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void SignRead_ClockAdvances_ReissuesExpiryAndSignatureWithoutChangingAssetIdentity()
    {
        var fixture = Create();
        var first = QueryHelpers.ParseQuery(new Uri(fixture.Service.SignRead(Key, "video")).Query);
        fixture.Time.Advance(TimeSpan.FromSeconds(61));
        var next = QueryHelpers.ParseQuery(new Uri(fixture.Service.SignRead(Key, "video")).Query);
        long.Parse(next["expires_at"].ToString(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(long.Parse(first["expires_at"].ToString(), System.Globalization.CultureInfo.InvariantCulture) + 61);
        next["signature"].ToString().Should().NotBe(first["signature"].ToString());
        next["public_id"].ToString().Should().Be(first["public_id"].ToString());
        fixture.Clients.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    [DataRow("image")]
    [DataRow("video")]
    [DataRow("raw")]
    public void CreateUploadGrant_PrivateAttemptKey_PreservesIdentityAndUsesCloudinarySignatureRules(string type)
    {
        var fixture = Create();
        var grant = fixture.Service.CreateUploadGrant(Key, type);
        grant.Url.Should().Be($"https://api.cloudinary.com/v1_1/unit-test-cloud/{type}/upload");
        grant.StorageKey.Should().Be(Key);
        grant.ResourceType.Should().Be(type);
        grant.Fields.Keys.Should().BeEquivalentTo("public_id", "type", "overwrite", "timestamp", "signature", "api_key");
        grant.Fields["public_id"].Should().Be("apcs/private/" + Key);
        grant.Fields["type"].Should().Be("authenticated");
        grant.Fields["overwrite"].Should().Be("false");
        grant.Fields["timestamp"].Should().Be(Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        grant.Fields["api_key"].Should().Be(fixture.Options.ApiKey);
        grant.Url.Should().NotContain(fixture.Options.ApiSecret);
        grant.Fields.Values.Should().NotContain(x => x.Contains(fixture.Options.ApiSecret, StringComparison.Ordinal));
        var parameters = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["timestamp"] = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["type"] = "authenticated", ["public_id"] = "apcs/private/" + Key, ["overwrite"] = "false"
        };
        grant.Fields["signature"].Should().Be(Signer(fixture.Options).Api.SignParameters(parameters));
        fixture.Service.CreateUploadGrant(Key, type).Fields["signature"].Should().Be(grant.Fields["signature"]);
        fixture.Clients.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void CreateUploadGrant_DifferentLeaseAndLaterClock_ChangesSignatureAndNeverOverwritesOldKey()
    {
        var fixture = Create();
        var first = fixture.Service.CreateUploadGrant(Key, "video");
        var otherLease = fixture.Service.CreateUploadGrant(Key.Replace("/lease/", "/replacement-lease/", StringComparison.Ordinal), "video");
        otherLease.Fields["public_id"].Should().Be("apcs/private/workflow-media/owner/run/job/replacement-lease/video");
        otherLease.Fields["signature"].Should().NotBe(first.Fields["signature"]);
        otherLease.Fields["overwrite"].Should().Be("false");
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        var later = fixture.Service.CreateUploadGrant(Key, "video");
        later.Fields["timestamp"].Should().Be((Now.ToUnixTimeSeconds() + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        later.Fields["signature"].Should().NotBe(first.Fields["signature"]);
        first.Fields["timestamp"].Should().Be(Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        fixture.Clients.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    private static Cloudinary Signer(CloudinaryOptions options) => new(new Account(options.CloudName, options.ApiKey, options.ApiSecret));
    private static (CloudinaryMediaStorage Service, CloudinaryOptions Options, FakeTimeProvider Time, Mock<IHttpClientFactory> Clients) Create()
    {
        var options = new CloudinaryOptions { CloudName = "unit-test-cloud", ApiKey = "synthetic-public-key", ApiSecret = "synthetic-private-secret", Folder = "/apcs//private/" };
        var time = new FakeTimeProvider(Now);
        var clients = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        return (new(Microsoft.Extensions.Options.Options.Create(options), time, clients.Object), options, time, clients);
    }
}
