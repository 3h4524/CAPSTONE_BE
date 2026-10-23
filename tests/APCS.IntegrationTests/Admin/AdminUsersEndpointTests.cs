using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;

namespace APCS.IntegrationTests.Admin;

/// <summary>
/// Covers the admin user-management lifecycle over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// User management is gated on <c>AuthConstants.AdminRole</c>; the authorization matrix proves the
/// gate while this suite proves the lifecycle: list contains
/// a freshly registered seller, read returns their row, suspend flips the status, unlock restores
/// it, and send-reset-password issues a reset link through the fake inbox without leaking the
/// caller. A seller token is stopped by the gate on every route.
/// </para>
/// </remarks>
[TestClass]
public sealed class AdminUsersEndpointTests
{
    private const string Route = "/api/admin/users";

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
    public async Task List_WithAnAdminSession_ContainsTheRegisteredSeller()
    {
        _host.EnsureAvailable();
        var seller = await TestAccount.SignInAsync(_host, TestAccount.Unique("managed") + "@apcs.test");
        using var client = await AdminClientAsync();

        using var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("email").GetString())
            .Should().Contain(seller.Email);
    }

    [TestMethod]
    public async Task GetById_WhenTheUserExists_ReturnsTheRow()
    {
        _host.EnsureAvailable();
        var seller = await TestAccount.SignInAsync(_host, TestAccount.Unique("managed") + "@apcs.test");
        using var client = await AdminClientAsync();

        using var response = await client.GetAsync($"{Route}/{seller.UserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString().Should().Be(seller.Email);
    }

    [TestMethod]
    public async Task GetById_WhenTheUserDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        using var client = await AdminClientAsync();

        using var response = await client.GetAsync($"{Route}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task SuspendThenUnlock_FlipsTheAccountStatusBothWays()
    {
        _host.EnsureAvailable();
        var seller = await TestAccount.SignInAsync(_host, TestAccount.Unique("managed") + "@apcs.test");
        using var client = await AdminClientAsync();

        using var suspend = await client.PostAsJsonAsync(
            $"{Route}/{seller.UserId}/suspend",
            new { reason = "Policy violation", durationDays = (int?)7 });

        suspend.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var suspended = await client.GetAsync($"{Route}/{seller.UserId}");
        (await suspended.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountStatus").GetString()
            .Should().Be("suspended");

        using var unlock = await client.PostAsync($"{Route}/{seller.UserId}/unlock", content: null);
        unlock.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var unlocked = await client.GetAsync($"{Route}/{seller.UserId}");
        (await unlocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountStatus").GetString()
            .Should().Be("active");
    }

    [TestMethod]
    public async Task SendResetPassword_WhenTheUserExists_AnswersNoContent()
    {
        _host.EnsureAvailable();
        var seller = await TestAccount.SignInAsync(_host, TestAccount.Unique("managed") + "@apcs.test");
        using var client = await AdminClientAsync();

        using var response = await client.PostAsync($"{Route}/{seller.UserId}/send-reset-password", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task Update_WhenThePayloadIsValid_RenamesTheUser()
    {
        _host.EnsureAvailable();
        var seller = await TestAccount.SignInAsync(_host, TestAccount.Unique("managed") + "@apcs.test");
        using var client = await AdminClientAsync();

        using var response = await client.PutAsJsonAsync(
            $"{Route}/{seller.UserId}",
            new
            {
                fullName = "Renamed By Admin",
                email = seller.Email,
                birthday = (DateTime?)null,
                avatarUrl = (string?)null,
                accountStatus = "active",
                roles = new[] { "Seller" }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fullName").GetString().Should().Be("Renamed By Admin");
    }

    [TestMethod]
    public async Task Plans_WithAnAdminSession_AnswersOk()
    {
        _host.EnsureAvailable();
        using var client = await AdminClientAsync();

        using var response = await client.GetAsync($"{Route}/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task AdminUserEndpoints_WhenTheCallerIsNotSignedIn_ReturnUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var list = await client.GetAsync(Route);
        await list.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task AdminUserEndpoints_WhenTheTokenCarriesTheSellerRole_ReturnForbidden()
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([TestAuth.SellerRole]);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var list = await client.GetAsync(Route);
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var detail = await client.GetAsync($"{Route}/{Guid.NewGuid()}");
        detail.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<HttpClient> AdminClientAsync()
    {
        var admin = await TestAccount.SignInAsync(_host, TestAccount.Unique("adminmgr") + "@apcs.test");
        var seeder = AuthTestSeeder.Create(_host.Factory.Services);
        var adminRoleId = await seeder.SeedRoleAsync(AuthConstants.AdminRole, AuthConstants.AdminRole);
        await seeder.GrantRoleAsync(admin.UserId, adminRoleId);

        var token = TestAuth.MintToken([TestAuth.AdminRole], admin.UserId);
        return _host.Factory.CreateAuthenticatedClient(token);
    }
}
