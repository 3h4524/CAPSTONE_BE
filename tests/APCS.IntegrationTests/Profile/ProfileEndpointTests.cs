using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;

namespace APCS.IntegrationTests.Profile;

/// <summary>
/// Covers the authenticated profile read and patch over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Profile is identity-only rather than role-gated: any signed-in account reads and edits only
/// its own row, so there is no second seller to forbid and no create or delete verb. The
/// meaningful guarantees are isolation between two accounts, partial-update semantics where an
/// omitted field keeps its value, and the allowlist rejection of unknown preference values.
/// </para>
/// <para>
/// A 403 case therefore does not exist for this surface by design. The suite pins 401 for an
/// anonymous caller and 400 for invalid input instead, and documents the absence of a role gate
/// so a future role requirement cannot slip in untested.
/// </para>
/// </remarks>
[TestClass]
public sealed class ProfileEndpointTests
{
    private const string Route = "/api/profile";

    private static IntegrationTestHost _host = null!;

    [ClassInitialize]
    public static async Task StartHostAsync(TestContext testContext)
    {
        _host = await IntegrationTestHost.StartAsync();
    }

    [ClassCleanup]
    public static async Task StopHostAsync()
    {
        await _host.DisposeAsync();
    }

    [TestInitialize]
    public async Task ResetDatabaseAsync()
    {
        await _host.ResetDatabaseAsync();
        await DedicatedDatabaseReset.TruncateConfiguredAsync();
    }

    [TestMethod]
    public async Task Get_WithASession_AnswersTheSignedInAccount()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("email").GetString().Should().Be(account.Email);
        body.GetProperty("fullName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task Get_WhenTwoSellersExist_AnswersEachOwnRow()
    {
        _host.EnsureAvailable();
        var first = await SignInAsync();
        var second = await SignInAsync();

        using var firstResponse = await first.Client.GetAsync(Route);
        using var secondResponse = await second.Client.GetAsync(Route);

        (await firstResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString().Should().Be(first.Email);
        (await secondResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString().Should().Be(second.Email);
    }

    [TestMethod]
    public async Task Patch_WhenThePayloadIsValid_UpdatesOnlySuppliedFields()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PatchAsJsonAsync(Route, new { fullName = "Renamed Seller" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("fullName").GetString().Should().Be("Renamed Seller");
        updated.GetProperty("email").GetString().Should().Be(account.Email, "an omitted field must keep its value");
    }

    [TestMethod]
    public async Task Patch_WhenShopFieldsAreSupplied_PersistsThem()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PatchAsJsonAsync(
            Route,
            new { shopName = "My Shop", shopDescription = "Short", timezone = "UTC", language = "en" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("shopName").GetString().Should().Be("My Shop");
        updated.GetProperty("timezone").GetString().Should().Be("UTC");
        updated.GetProperty("language").GetString().Should().Be("en");
    }

    [TestMethod]
    public async Task Patch_WhenTheTimezoneIsUnknown_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PatchAsJsonAsync(Route, new { timezone = "Mars/Olympus" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task Patch_WhenTheEmailIsMalformed_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PatchAsJsonAsync(Route, new { email = "not-an-email" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task Patch_WhenTheEmailBelongsToAnotherSeller_ReturnsConflict()
    {
        _host.EnsureAvailable();
        var first = await SignInAsync();
        var second = await SignInAsync();

        using var response = await first.Client.PatchAsJsonAsync(Route, new { email = second.Email });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Conflict, ErrorCodes.EmailAlreadyExists);
    }

    [TestMethod]
    public async Task ProfileEndpoints_WhenTheCallerIsNotSignedIn_ReturnUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var get = await client.GetAsync(Route);
        await get.ShouldFailAsync(HttpStatusCode.Unauthorized);

        using var patch = await client.PatchAsJsonAsync(Route, new { fullName = "Anonymous" });
        await patch.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task ProfileEndpoints_WhenTheTokenCarriesAnyRole_ReachTheController()
    {
        _host.EnsureAvailable();
        var sellerToken = TestAuth.MintToken([TestAuth.SellerRole]);
        using var sellerClient = _host.Factory.CreateAuthenticatedClient(sellerToken);
        using var sellerGet = await sellerClient.GetAsync(Route);
        sellerGet.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "profile is identity-only and names no role");

        var adminToken = TestAuth.MintToken([TestAuth.AdminRole]);
        using var adminClient = _host.Factory.CreateAuthenticatedClient(adminToken);
        using var adminGet = await adminClient.GetAsync(Route);
        adminGet.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "no role may be forbidden from an identity-only route");
    }

    private static Task<AuthenticatedAccount> SignInAsync() =>
        TestAccount.SignInAsync(_host, TestAccount.Unique("profile") + "@apcs.test");
}
