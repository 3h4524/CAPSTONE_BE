using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Npgsql;

namespace APCS.IntegrationTests.ApiKeys;

/// <summary>
/// Covers the seller API-key surface over real HTTP without reaching real providers.
/// </summary>
/// <remarks>
/// <para>
/// Provider validation normally calls the vendor over HTTP, but the host swaps that client for
/// one that always throws, and the credential adapter lets the throw bubble to a 500. A live
/// key check therefore has no meaningful integration assertion here. What remains worth proving
/// is everything around that boundary: provider listing, owned-key listing, request validation,
/// the not-found branches that run before any network call, and a seeded-key rename and delete
/// that deliberately omit the key value so no outbound call is attempted.
/// </para>
/// <para>
/// The outbound block is recorded as a follow-up rather than asserted as desired behavior: a
/// fake credential adapter would let the happy-path create run without real vendors.
/// </para>
/// </remarks>
[TestClass]
public sealed class ApiKeyEndpointTests
{
    private const string Route = "/api/api-keys";
    private const string ProvidersRoute = "/api/api-keys/providers";

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
    public async Task Providers_WithASession_ListsTheSupportedProviders()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync(ProvidersRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var providers = await response.Content.ReadFromJsonAsync<JsonElement>();
        providers.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Should().Contain(["openai", "replicate", "printify"]);
    }

    [TestMethod]
    public async Task List_WhenNoKeyExists_ReturnsEmptyItems()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task List_AfterSeeding_ContainsTheOwnedKey()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var keyId = await SeedKeyAsync(account.UserId, "openai", "Seeded");

        using var response = await account.Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should().Contain(keyId);
    }

    [TestMethod]
    public async Task Update_WhenOnlyTheLabelChanges_RenamesWithoutCallingTheProvider()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var keyId = await SeedKeyAsync(account.UserId, "openai", "Before");

        using var response = await account.Client.PutAsJsonAsync(
            $"{Route}/{keyId}",
            new { provider = "openai", name = "After", environment = "Sandbox", apiKey = (string?)null, confirmed = true });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var reread = await account.Client.GetAsync(Route);
        var body = await reread.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == keyId)
            .GetProperty("label").GetString().Should().Be("After");
    }

    [TestMethod]
    public async Task Delete_WhenTheKeyIsOwned_ReturnsNoContent()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var keyId = await SeedKeyAsync(account.UserId, "replicate", "Removable");

        using var response = await account.Client.DeleteAsync($"{Route}/{keyId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var reread = await account.Client.GetAsync(Route);
        (await reread.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should().NotContain(keyId);
    }

    [TestMethod]
    public async Task Save_WhenTheProviderIsUnsupported_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            Route,
            new { provider = "not-a-provider", name = "Bad", apiKey = "validkey123", confirmed = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Save_WhenTheConfirmationIsMissing_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            Route,
            new { provider = "openai", name = "Unconfirmed", apiKey = "validkey123", confirmed = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Update_WhenTheKeyDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PutAsJsonAsync(
            $"{Route}/{Guid.NewGuid()}",
            new { provider = "openai", name = "Missing", apiKey = "validkey123", confirmed = true });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, "ApiKeys.NotFound");
    }

    [TestMethod]
    public async Task Delete_WhenTheKeyDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.DeleteAsync($"{Route}/{Guid.NewGuid()}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, "ApiKeys.NotFound");
    }

    [TestMethod]
    public async Task Validate_WhenTheKeyDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsync($"{Route}/{Guid.NewGuid()}/validate", content: null);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, "ApiKeys.NotFound");
    }

    [TestMethod]
    public async Task ApiKeyEndpoints_WhenTheCallerIsNotSignedIn_ReturnUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var providers = await client.GetAsync(ProvidersRoute);
        await providers.ShouldFailAsync(HttpStatusCode.Unauthorized);

        using var list = await client.GetAsync(Route);
        await list.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task ApiKeyEndpoints_WhenTheTokenCarriesTheAdminRole_ReturnForbidden()
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([TestAuth.AdminRole]);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var providers = await client.GetAsync(ProvidersRoute);
        providers.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var list = await client.GetAsync(Route);
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Task<AuthenticatedAccount> SignInAsync() =>
        TestAccount.SignInAsync(_host, TestAccount.Unique("apikey") + "@apcs.test");

    private static async Task<Guid> SeedKeyAsync(Guid userId, string provider, string label)
    {
        var id = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO api_keys
                (id, user_id, service_provider, key_identifier, key_value_encrypted, key_last_4,
                 is_active, auth_type, environment, created_at, last_checked_at, last_check_succeeded)
            VALUES
                (@id, @userId, @provider, @label, 'dummy-encrypted', '1234',
                 true, 'api_key', 'Production', now(), now(), true);
            """,
            connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("label", label);

        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");
}
