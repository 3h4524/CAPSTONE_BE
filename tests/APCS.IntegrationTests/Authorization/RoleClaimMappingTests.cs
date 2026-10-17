using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace APCS.IntegrationTests.Authorization;

/// <summary>
/// Pins down where a role claim comes from, which is the one part of the authorization gate the
/// request-level matrix cannot establish on its own.
/// </summary>
/// <remarks>
/// <para>
/// The gate compares the role claim in the token against the literal names in
/// <c>[Authorize(Roles = ...)]</c>, while the claim itself is projected from
/// <c>AccountRepository.GetActiveRoleCodesAsync</c>, which selects <c>Role.Code</c>. The administrator
/// role editor, by contrast, matches and reports roles by <c>Role.Name</c>. When a role row carries a
/// name and a code that differ, those two views of the same grant stop agreeing, and nothing in the
/// request pipeline notices.
/// </para>
/// <para>
/// Every token here is issued by a real sign-in, so the claim is produced by the production
/// projection rather than by <see cref="TestAuth"/>.
/// </para>
/// </remarks>
[TestClass]
public sealed class RoleClaimMappingTests
{
    private const string Password = "RoleClaimFixture1!";

    /// <summary>
    /// A role whose code is what the gate accepts while its name is not. Nothing seeds the role table
    /// in this repository, so the row is the only definition of what a name and a code may look like.
    /// </summary>
    private const string GateRoleCode = TestAuth.SuperAdminRole;

    private const string GateRoleName = "Super Administrator";

    /// <summary>
    /// A role that answers to the administrator name every admin screen displays while its code - the
    /// value the gate and the token actually use - is something else entirely.
    /// </summary>
    private const string MisnamedAdminRoleCode = "Administrator";

    private const string MisnamedAdminRoleName = AuthConstants.AdminRole;

    private static IntegrationTestHost _host = null!;

    private static AuthTestSeeder _seeder = null!;

    private static Guid _gateRoleId;

    private static Guid _misnamedAdminRoleId;

    private static readonly List<Guid> _seededUserIds = [];

    [ClassInitialize]
    public static async Task StartHostAsync(TestContext testContext)
    {
        _host = await IntegrationTestHost.StartAsync();
        _host.EnsureAvailable();

        _seeder = AuthTestSeeder.Create(_host.Factory.Services);
        _gateRoleId = await _seeder.SeedRoleAsync(GateRoleCode, GateRoleName);
        _misnamedAdminRoleId = await _seeder.SeedRoleAsync(MisnamedAdminRoleCode, MisnamedAdminRoleName);
    }

    [ClassCleanup]
    public static async Task StopHostAsync()
    {
        if (_seeder is not null)
        {
            await _seeder.DeleteUsersAsync(_seededUserIds);
            await _seeder.DeleteRolesAsync([GateRoleCode, MisnamedAdminRoleCode]);
        }

        await _host.DisposeAsync();
    }

    [TestMethod]
    public async Task Login_WhenTheRoleNameDiffersFromTheRoleCode_IssuesTheCodeAsTheRoleClaim()
    {
        var userId = await SeedUserAsync("claim-source@apcs.test", _gateRoleId);

        var accessToken = await SignInAsync("claim-source@apcs.test");

        (ReadRoleClaims(accessToken)).Should().Equal(GateRoleCode);
        (await _seeder.ReadActiveRoleCodesAsync(userId)).Should().Equal(GateRoleCode);
        GateRoleName.Should().NotBe(GateRoleCode, "the point of this role row is that its name and code differ");
    }

    [TestMethod]
    public async Task AdminUsersEndpoint_WhenTheRoleCodeMatchesTheGateClaim_ReachesTheController()
    {
        await SeedUserAsync("claim-passes@apcs.test", _gateRoleId);
        var accessToken = await SignInAsync("claim-passes@apcs.test");

        using var client = _host.Factory.CreateAuthenticatedClient(accessToken);
        using var response = await client.GetAsync("/api/admin/users");

        response.StatusCode.Should().NotBe(
            HttpStatusCode.Forbidden,
            "the claim carries the code the gate lists, so only the framework gate could have refused it");
    }

    [TestMethod]
    public async Task AdminUsersEndpoint_WhenTheTokenCarriesTheSuperAdminRole_ReachesTheController()
    {
        var accessToken = TestAuth.MintToken([TestAuth.SuperAdminRole], await SeedUserAsync("literal@apcs.test"));

        using var client = _host.Factory.CreateAuthenticatedClient(accessToken);
        using var response = await client.GetAsync("/api/admin/users");

        response.StatusCode.Should().NotBe(
            HttpStatusCode.Forbidden,
            "AdminUsersController declares its gate as the literal \"Admin,Super Admin\", which splits into two accepted roles");
    }

    [TestMethod]
    public async Task AdminUsersEndpoint_WhenTheSuperAdminRoleIsWrittenWithoutASpace_ReturnsForbidden()
    {
        var accessToken = TestAuth.MintToken(["SuperAdmin"], await SeedUserAsync("literal-gap@apcs.test"));

        using var client = _host.Factory.CreateAuthenticatedClient(accessToken);
        using var response = await client.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the gate matches its role list verbatim, so a role seeded as \"SuperAdmin\" would be refused while the code claims otherwise");
    }

    [TestMethod]
    public async Task AdminRoleGrant_WhenTheRoleNameIsAdminButTheCodeIsNot_IssuesACodeClaimTheGateRefuses()
    {
        var targetId = await SeedUserAsync("misnamed-admin@apcs.test");
        var adminToken = TestAuth.MintToken([AuthConstants.AdminRole], Guid.NewGuid());

        using var adminClient = _host.Factory.CreateAuthenticatedClient(adminToken);
        using var grantResponse = await adminClient.PutAsJsonAsync(
            $"/api/admin/users/{targetId}",
            new
            {
                fullName = "Misnamed Admin",
                email = "misnamed-admin@apcs.test",
                birthday = (DateTime?)null,
                avatarUrl = (string?)null,
                accountStatus = "Active",
                roles = new[] { MisnamedAdminRoleName }
            });

        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await grantResponse.Content.ReadFromJsonAsync<AdminRoleAssignment>())!
            .Roles.Should().Equal([MisnamedAdminRoleName], "the admin role editor reports the role by name");

        var accessToken = await SignInAsync("misnamed-admin@apcs.test");
        var claims = ReadRoleClaims(accessToken);
        claims.Should().Equal([MisnamedAdminRoleCode], "the repository projects Role.Code into the claim");
        claims.Should().NotContain(MisnamedAdminRoleName, "the gate would then have no claim to match");

        using var client = _host.Factory.CreateAuthenticatedClient(accessToken);
        using var metricsResponse = await client.GetAsync("/api/admin/dashboard/metrics");
        using var usersResponse = await client.GetAsync("/api/admin/users");

        metricsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        usersResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> SeedUserAsync(string email, params Guid[] roleIds)
    {
        var id = Guid.NewGuid();
        var user = new User { Id = id, Email = email, FullName = email, PasswordHash = string.Empty };
        var hasher = _host.Factory.Services.GetRequiredService<IPasswordHasher<User>>();

        await _seeder.SeedActiveUserAsync(email, email, hasher.HashPassword(user, Password), id);

        lock (_seededUserIds)
        {
            _seededUserIds.Add(id);
        }

        foreach (var roleId in roleIds)
        {
            await _seeder.GrantRoleAsync(id, roleId);
        }

        return id;
    }

    private static async Task<string> SignInAsync(string email)
    {
        using var client = _host.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the fixture seeds an active, verified account");

        var accessToken = ReadAccessTokenCookie(response);

        accessToken.Should().NotBeNullOrWhiteSpace("the access token travels in the HttpOnly cookie, not in the body");

        return accessToken!;
    }

    private static string? ReadAccessTokenCookie(HttpResponseMessage response)
    {
        foreach (var header in response.Headers.GetValues("Set-Cookie"))
        {
            if (!header.StartsWith(AuthConstants.AccessTokenCookieName + "=", StringComparison.Ordinal))
            {
                continue;
            }

            var pair = header.Split(';', 2)[0];
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            return separator < 0 ? null : pair[(separator + 1)..];
        }

        return null;
    }

    private static IReadOnlyList<string> ReadRoleClaims(string accessToken)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        return
        [
            .. token.Claims
                .Where(claim => claim.Type.EndsWith("/role", StringComparison.Ordinal) || claim.Type == "role")
                .Select(claim => claim.Value)
        ];
    }

    private sealed record AdminRoleAssignment(IReadOnlyList<string> Roles);
}