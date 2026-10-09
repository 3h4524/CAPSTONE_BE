using System.Net;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace APCS.IntegrationTests.Authorization;

/// <summary>
/// Drives every <c>[Authorize]</c> endpoint in the API through the real middleware pipeline, so the
/// role gate is enforced by the framework rather than asserted against an isolated controller.
/// </summary>
/// <remarks>
/// <para>
/// The gate is checked from both directions. A token that lacks the required role has to be stopped
/// by <c>AuthorizationMiddleware</c> with 403 before the action runs, and a token that carries it has
/// to reach the action, which is observable as an answer that is neither 401 nor 403. Whatever the
/// action itself decides - 200, 204, 400 from model binding, 404 from an empty table - belongs to
/// that endpoint's own tests and is deliberately not constrained here.
/// </para>
/// <para>
/// Two identities back the tokens. The administrator identities hold the seller grant in the
/// database as well, so an administrator token rejected by a seller endpoint can only have been
/// stopped by the role claim: the account state would have let it through. Without that, the
/// negative cases would quietly test the in-service account check that some services run on top of
/// the framework gate, and the framework gate itself would stay unproven.
/// </para>
/// <para>
/// <see cref="TestAuth"/> signs every token with the production <c>JwtService</c>, so the claim
/// serialization under test is the same one a real sign-in produces. A hand-written token would test
/// a different serializer and could silently diverge on claim mapping.
/// </para>
/// </remarks>
[TestClass]
public sealed class AuthorizationMatrixTests
{
    /// <summary>
    /// Stands in for a route parameter so a route template still matches. Authorization is decided
    /// after routing, so a route that fails to match never reaches the gate and would prove nothing.
    /// </summary>
    private const string PlaceholderId = "11111111-1111-1111-1111-111111111111";

    private const string AdminRole = AuthConstants.AdminRole;

    private const string SellerRole = AuthConstants.UserRole;

    private const string SuperAdminRole = TestAuth.SuperAdminRole;

    private const string Password = "AuthorizationFixture1!";

    private static readonly IReadOnlyDictionary<string, HttpMethod> Methods =
        new Dictionary<string, HttpMethod>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET"] = HttpMethod.Get,
            ["POST"] = HttpMethod.Post,
            ["PUT"] = HttpMethod.Put,
            ["PATCH"] = HttpMethod.Patch,
            ["DELETE"] = HttpMethod.Delete
        };

    private static IntegrationTestHost _host = null!;

    private static AuthTestSeeder _seeder = null!;

    private static Guid _sellerUserId;

    private static Guid _adminUserId;

    private static Guid _superAdminUserId;

    /// <summary>Gets the endpoints gated on <c>AuthConstants.AdminRole</c> alone.</summary>
    /// <returns>Every HTTP verb and route of the dashboard, subscription-plan, and ticket admins.</returns>
    public static string[][] AdminGatedEndpoints =>
    [
        ["GET", "/api/admin/dashboard/metrics"],
        ["GET", "/api/admin/dashboard/export"],
        ["GET", "/api/admin/subscription-plans"],
        ["GET", $"/api/admin/subscription-plans/{PlaceholderId}"],
        ["POST", "/api/admin/subscription-plans"],
        ["PUT", $"/api/admin/subscription-plans/{PlaceholderId}"],
        ["DELETE", $"/api/admin/subscription-plans/{PlaceholderId}"],
        ["GET", "/api/admin/support-tickets"],
        ["GET", $"/api/admin/support-tickets/{PlaceholderId}"],
        ["PATCH", $"/api/admin/support-tickets/{PlaceholderId}"],
        ["POST", $"/api/admin/support-tickets/{PlaceholderId}/replies"]
    ];

    /// <summary>Gets the endpoints gated on <c>AuthConstants.UserRole</c>.</summary>
    /// <returns>Every HTTP verb and route of the seller-facing feature controllers.</returns>
    public static string[][] SellerGatedEndpoints =>
    [
        ["GET", "/api/api-keys/providers"],
        ["GET", "/api/api-keys"],
        ["POST", "/api/api-keys"],
        ["PUT", $"/api/api-keys/{PlaceholderId}"],
        ["DELETE", $"/api/api-keys/{PlaceholderId}"],
        ["POST", $"/api/api-keys/{PlaceholderId}/validate"],
        ["GET", $"/api/batch-jobs/{PlaceholderId}/mockups"],
        ["PUT", $"/api/batch-jobs/{PlaceholderId}/mockups"],
        ["GET", $"/api/batch-job-products/{PlaceholderId}/prompt/default"],
        ["GET", $"/api/batch-job-products/{PlaceholderId}/prompt"],
        ["PUT", $"/api/batch-job-products/{PlaceholderId}/prompt"],
        ["DELETE", $"/api/batch-job-products/{PlaceholderId}/prompt"],
        ["GET", "/api/batches"],
        ["POST", "/api/batches"],
        ["PUT", $"/api/batches/{PlaceholderId}"],
        ["DELETE", $"/api/batches/{PlaceholderId}"],
        ["POST", $"/api/batches/{PlaceholderId}/approve"],
        ["GET", $"/api/batches/{PlaceholderId}/products"],
        ["POST", $"/api/batches/{PlaceholderId}/products"],
        ["POST", $"/api/batches/{PlaceholderId}/products/import"],
        ["PUT", $"/api/batches/{PlaceholderId}/products/{PlaceholderId}"],
        ["DELETE", $"/api/batches/{PlaceholderId}/products/{PlaceholderId}"],
        ["GET", "/api/design-templates"],
        ["GET", "/api/design-templates/options"],
        ["GET", $"/api/design-templates/{PlaceholderId}"],
        ["POST", "/api/design-templates"],
        ["POST", $"/api/design-templates/{PlaceholderId}/clone"],
        ["PUT", $"/api/design-templates/{PlaceholderId}"],
        ["DELETE", $"/api/design-templates/{PlaceholderId}"],
        ["GET", "/api/mockup-templates"],
        ["GET", "/api/style-art-presets"],
        ["GET", $"/api/style-art-presets/{PlaceholderId}"],
        ["POST", "/api/style-art-presets"],
        ["POST", "/api/style-art-presets/quick"],
        ["PUT", $"/api/style-art-presets/{PlaceholderId}"],
        ["DELETE", $"/api/style-art-presets/{PlaceholderId}"],
        ["GET", "/api/subscriptions/overview"],
        ["POST", "/api/subscriptions/checkout"],
        ["GET", $"/api/subscriptions/checkout/{PlaceholderId}/status"],
        ["POST", $"/api/subscriptions/checkout/{PlaceholderId}/cancel"],
        ["POST", "/api/subscriptions/upgrade"],
        ["POST", "/api/subscriptions/downgrade"],
        ["POST", "/api/subscriptions/downgrade/cancel"],
        ["GET", $"/api/subscriptions/invoices/{PlaceholderId}/download"],
        ["POST", "/api/support-tickets"],
        ["GET", "/api/support-tickets"],
        ["GET", $"/api/support-tickets/{PlaceholderId}"],
        ["POST", $"/api/support-tickets/{PlaceholderId}/replies"],
        ["PUT", $"/api/support-tickets/{PlaceholderId}/satisfaction-rating"],
        ["GET", "/api/usage"]
    ];

    /// <summary>Gets the endpoints <c>AdminUsersController</c> gates on two literal role names.</summary>
    /// <returns>Every HTTP verb and route of the admin user management surface.</returns>
    public static string[][] MultiRoleAdminEndpoints =>
    [
        ["GET", "/api/admin/users"],
        ["GET", "/api/admin/users/plans"],
        ["GET", $"/api/admin/users/{PlaceholderId}"],
        ["PUT", $"/api/admin/users/{PlaceholderId}"],
        ["POST", $"/api/admin/users/{PlaceholderId}/suspend"],
        ["POST", $"/api/admin/users/{PlaceholderId}/unlock"],
        ["POST", $"/api/admin/users/{PlaceholderId}/send-reset-password"]
    ];

    /// <summary>Gets the endpoints that require an identity but name no role.</summary>
    /// <returns>Every HTTP verb and route of the profile and session endpoints.</returns>
    public static string[][] IdentityOnlyEndpoints =>
    [
        ["GET", "/api/profile"],
        ["PATCH", "/api/profile"],
        ["GET", "/api/auth/me"],
        ["POST", "/api/auth/logout"],
        ["POST", "/api/auth/change-password"]
    ];

    /// <summary>Gets every endpoint in the API that sits behind an authorization gate.</summary>
    /// <returns>The union of the role-gated and identity-only routes.</returns>
    public static string[][] AuthorizedEndpoints =>
        Combine(AdminGatedEndpoints, SellerGatedEndpoints, MultiRoleAdminEndpoints, IdentityOnlyEndpoints);

    [ClassInitialize]
    public static async Task StartHostAsync(TestContext testContext)
    {
        _host = await IntegrationTestHost.StartAsync();
        _host.EnsureAvailable();

        _seeder = AuthTestSeeder.Create(_host.Factory.Services);

        var sellerRoleId = await _seeder.SeedRoleAsync(SellerRole, SellerRole);
        var adminRoleId = await _seeder.SeedRoleAsync(AdminRole, AdminRole);

        _sellerUserId = await SeedIdentityAsync("authz-seller@apcs.test", sellerRoleId);
        _adminUserId = await SeedIdentityAsync("authz-admin@apcs.test", sellerRoleId, adminRoleId);
        _superAdminUserId = await SeedIdentityAsync("authz-super-admin@apcs.test", sellerRoleId, adminRoleId);
    }

    [ClassCleanup]
    public static async Task StopHostAsync()
    {
        if (_seeder is not null)
        {
            await _seeder.DeleteUsersAsync([_sellerUserId, _adminUserId, _superAdminUserId]);
            await _seeder.DeleteRolesAsync([SellerRole, AdminRole, SuperAdminRole]);
        }

        await _host.DisposeAsync();
    }

    [TestInitialize]
    public Task ResetDatabaseAsync() => _host.ResetDatabaseAsync();

    [TestMethod]
    [DynamicData(nameof(AuthorizedEndpoints))]
    public async Task Endpoint_WhenNoCredentialIsPresented_ReturnsUnauthorized(string method, string path)
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "{0} {1} is gated and must reject an anonymous request before routing reaches the action",
            method,
            path);
    }

    [TestMethod]
    [DynamicData(nameof(AdminGatedEndpoints))]
    public async Task AdminEndpoint_WhenTheTokenCarriesNoRoleClaim_ReturnsForbidden(string method, string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([], _sellerUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "{0} {1} is gated on the admin role, which a token without any role claim does not satisfy",
            method,
            path);
    }

    [TestMethod]
    [DynamicData(nameof(AdminGatedEndpoints))]
    public async Task AdminEndpoint_WhenTheTokenCarriesTheSellerRole_ReturnsForbidden(string method, string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([SellerRole], _sellerUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "{0} {1} is gated on the admin role, which the seller claim does not satisfy",
            method,
            path);
    }

    [TestMethod]
    [DynamicData(nameof(AdminGatedEndpoints))]
    public async Task AdminEndpoint_WhenTheTokenCarriesTheAdminRole_ReachesTheController(string method, string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([AdminRole], _adminUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        ShouldReachTheController(response, method, path, AdminRole);
    }

    [TestMethod]
    [DynamicData(nameof(SellerGatedEndpoints))]
    public async Task SellerEndpoint_WhenTheTokenCarriesNoRoleClaim_ReturnsForbidden(string method, string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([], _sellerUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "{0} {1} is gated on the seller role, which a token without any role claim does not satisfy",
            method,
            path);
    }

    [TestMethod]
    [DynamicData(nameof(SellerGatedEndpoints))]
    public async Task SellerEndpoint_WhenTheTokenCarriesTheAdminRole_ReturnsForbidden(string method, string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([AdminRole], _adminUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "{0} {1} is gated on the seller role; the account holds the seller grant, so only the admin claim can be the reason it was stopped",
            method,
            path);
    }

    [TestMethod]
    [DynamicData(nameof(SellerGatedEndpoints))]
    public async Task SellerEndpoint_WhenTheTokenCarriesTheSellerRole_ReachesTheController(
        string method,
        string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([SellerRole], _sellerUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        ShouldReachTheController(response, method, path, SellerRole);
    }

    [TestMethod]
    [DynamicData(nameof(MultiRoleAdminEndpoints))]
    public async Task MultiRoleAdminEndpoint_WhenTheTokenCarriesNoRoleClaim_ReturnsForbidden(
        string method,
        string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([], _sellerUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "{0} {1} is gated on one of two admin roles, which a token without any role claim does not satisfy",
            method,
            path);
    }

    [TestMethod]
    [DynamicData(nameof(MultiRoleAdminEndpoints))]
    public async Task MultiRoleAdminEndpoint_WhenTheTokenCarriesTheSellerRole_ReturnsForbidden(
        string method,
        string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([SellerRole], _sellerUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "{0} {1} is gated on one of two admin roles, which the seller claim does not satisfy",
            method,
            path);
    }

    [TestMethod]
    [DynamicData(nameof(MultiRoleAdminEndpoints))]
    public async Task MultiRoleAdminEndpoint_WhenTheTokenCarriesTheAdminRole_ReachesTheController(
        string method,
        string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([AdminRole], _adminUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        ShouldReachTheController(response, method, path, AdminRole);
    }

    [TestMethod]
    [DynamicData(nameof(MultiRoleAdminEndpoints))]
    public async Task MultiRoleAdminEndpoint_WhenTheTokenCarriesTheSuperAdminRole_ReachesTheController(
        string method,
        string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([SuperAdminRole], _superAdminUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        ShouldReachTheController(response, method, path, SuperAdminRole);
    }

    [TestMethod]
    [DynamicData(nameof(MultiRoleAdminEndpoints))]
    public async Task MultiRoleAdminEndpoint_WhenTheTokenCarriesBothAdminRoles_ReachesTheController(
        string method,
        string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([AdminRole, SuperAdminRole], _superAdminUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        ShouldReachTheController(response, method, path, $"{AdminRole} + {SuperAdminRole}");
    }

    [TestMethod]
    [DynamicData(nameof(IdentityOnlyEndpoints))]
    public async Task IdentityOnlyEndpoint_WhenTheTokenCarriesNoRoleClaim_ReachesTheController(
        string method,
        string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([], _sellerUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        ShouldReachTheController(response, method, path, "no role");
    }

    [TestMethod]
    [DynamicData(nameof(IdentityOnlyEndpoints))]
    public async Task IdentityOnlyEndpoint_WhenTheTokenCarriesTheSellerRole_ReachesTheController(
        string method,
        string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([SellerRole], _sellerUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        ShouldReachTheController(response, method, path, SellerRole);
    }

    [TestMethod]
    [DynamicData(nameof(AuthorizedEndpoints))]
    public async Task Endpoint_WhenTheTokenHasExpired_ReturnsUnauthorized(string method, string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintExpiredToken([AdminRole], _adminUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "{0} {1} must reject a token whose lifetime has elapsed, whatever role it carries",
            method,
            path);
    }

    [TestMethod]
    [DynamicData(nameof(AuthorizedEndpoints))]
    public async Task Endpoint_WhenTheTokenIsSignedWithAnUntrustedKey_ReturnsUnauthorized(string method, string path)
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintTokenWithWrongSignature([AdminRole], _adminUserId);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "{0} {1} must reject a token the host did not sign, whatever role it carries",
            method,
            path);
    }

    private static async Task<Guid> SeedIdentityAsync(string email, params Guid[] roleIds)
    {
        var id = Guid.NewGuid();
        var user = new User { Id = id, Email = email, FullName = email, PasswordHash = string.Empty };
        var hasher = _host.Factory.Services.GetRequiredService<IPasswordHasher<User>>();

        await _seeder.SeedActiveUserAsync(email, email, hasher.HashPassword(user, Password), id);

        foreach (var roleId in roleIds)
        {
            await _seeder.GrantRoleAsync(id, roleId);
        }

        return id;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var request = new HttpRequestMessage(Methods[method], path);

        return await client.SendAsync(request);
    }

    private static void ShouldReachTheController(
        HttpResponseMessage response,
        string method,
        string path,
        string role)
    {
        response.StatusCode.Should().NotBe(
            HttpStatusCode.Unauthorized,
            "{0} {1} must accept a token carrying {2}, so an authentication failure would mean the credential is rejected",
            method,
            path,
            role);

        response.StatusCode.Should().NotBe(
            HttpStatusCode.Forbidden,
            "{0} {1} must accept a token carrying {2}, so the authorization gate rejected a role it declares",
            method,
            path,
            role);
    }

    private static string[][] Combine(params string[][][] tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        return [.. tables.SelectMany(table => table)];
    }
}