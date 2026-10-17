using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;

namespace APCS.IntegrationTests.Batches;

/// <summary>
/// Covers the seller batch lifecycle over real HTTP against the real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Batches are the entry point of the creation pipeline, and their ownership rule lives in the
/// service query filters rather than in the role gate: a seller sees only rows carrying their own
/// identifier, a duplicate name collides only within that scope, and a missing row answers 404
/// instead of leaking another seller's data. Those filters cannot be proven with a mocked
/// repository, so every step here goes through the public endpoints.
/// </para>
/// <para>
/// Product rows ride the same ownership check through their parent batch. The suite therefore
/// walks both lifecycles in one place: batch create to delete, plus product add to delete, with
/// the validation, authentication, and role-gate branches pinned alongside.
/// </para>
/// </remarks>
[TestClass]
public sealed class BatchEndpointTests
{
    private const string Route = "/api/batches";

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
    public async Task Create_WhenThePayloadIsValid_ReturnsOk()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = TestAccount.Unique("Batch");

        using var response = await account.Client.PostAsJsonAsync(
            Route,
            new { name, description = "Spring collection", defaultNiche = "Cats", defaultProductType = "tshirt" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("name").GetString().Should().Be(name);
        created.GetProperty("status").GetString().Should().Be("draft");
    }

    [TestMethod]
    public async Task List_AfterCreate_ContainsTheBatch()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = TestAccount.Unique("Batch");
        await CreateAsync(account.Client, name);

        using var response = await account.Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await response.Content.ReadFromJsonAsync<JsonElement>();
        listed.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should().Contain(name);
    }

    [TestMethod]
    public async Task Update_WhenThePayloadIsValid_RenamesTheBatch()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Before"));
        var id = await GetIdAsync(account.Client, name);
        var renamed = TestAccount.Unique("After");

        using var response = await account.Client.PutAsJsonAsync(
            $"{Route}/{id}",
            new { name = renamed, description = "Rewritten", defaultNiche = "Dogs", defaultProductType = "hoodie" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be(renamed);
    }

    [TestMethod]
    public async Task Delete_WhenTheBatchIsOwned_ReturnsNoContent()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Removable"));
        var id = await GetIdAsync(account.Client, name);

        using var response = await account.Client.DeleteAsync($"{Route}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var reread = await account.Client.GetAsync(Route);
        (await reread.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should().NotContain(name);
    }

    [TestMethod]
    public async Task Create_WhenTheNameIsMissing_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            Route,
            new { name = string.Empty, description = "No name" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task Create_WhenTheNameIsAlreadyTaken_ReturnsConflict()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Taken"));

        using var response = await account.Client.PostAsJsonAsync(
            Route,
            new { name, description = "Duplicate" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Conflict, "Batches.Duplicate");
    }

    [TestMethod]
    public async Task Update_WhenTheBatchDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PutAsJsonAsync(
            $"{Route}/{Guid.NewGuid()}",
            new { name = TestAccount.Unique("Missing") });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, "Batches.NotFound");
    }

    [TestMethod]
    public async Task Delete_WhenTheBatchDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.DeleteAsync($"{Route}/{Guid.NewGuid()}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, "Batches.NotFound");
    }

    [TestMethod]
    public async Task GetProducts_WhenTheBatchIsEmpty_ReturnsEmptyList()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Empty"));
        var id = await GetIdAsync(account.Client, name);

        using var response = await account.Client.GetAsync($"{Route}/{id}/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task ProductLifecycle_AddReadUpdateDelete_WalksEveryStep()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("WithProducts"));
        var batchId = await GetIdAsync(account.Client, name);

        using var add = await account.Client.PostAsJsonAsync(
            $"{Route}/{batchId}/products",
            new
            {
                name = "Cool Tee",
                productType = "tshirt",
                niche = "Cats",
                keywords = new[] { "cat", "funny" },
                productDescription = "A funny cat tee",
                sourceNotes = "manual"
            });

        add.StatusCode.Should().Be(HttpStatusCode.OK);
        var productId = (await add.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var list = await account.Client.GetAsync($"{Route}/{batchId}/products");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should().Contain(productId);

        using var edit = await account.Client.PutAsJsonAsync(
            $"{Route}/{batchId}/products/{productId}",
            new
            {
                name = "Cooler Tee",
                productType = "tshirt",
                niche = "Cats",
                keywords = new[] { "cat" },
                productDescription = "Rewritten",
                sourceNotes = "manual"
            });

        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await edit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("Cooler Tee");

        using var delete = await account.Client.DeleteAsync($"{Route}/{batchId}/products/{productId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task AddProduct_WhenTheProductTypeIsInvalid_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Guarded"));
        var batchId = await GetIdAsync(account.Client, name);

        using var response = await account.Client.PostAsJsonAsync(
            $"{Route}/{batchId}/products",
            new { name = "Bad", productType = "not-a-type", niche = "Cats", keywords = new[] { "cat" } });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task BatchEndpoints_WhenTheCallerIsNotSignedIn_ReturnUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var list = await client.GetAsync(Route);
        await list.ShouldFailAsync(HttpStatusCode.Unauthorized);

        using var create = await client.PostAsJsonAsync(Route, new { name = "Anonymous" });
        await create.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task BatchEndpoints_WhenTheTokenCarriesTheAdminRole_ReturnForbidden()
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([TestAuth.AdminRole]);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var list = await client.GetAsync(Route);
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var create = await client.PostAsJsonAsync(Route, new { name = "AdminBatch" });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Task<AuthenticatedAccount> SignInAsync() =>
        TestAccount.SignInAsync(_host, TestAccount.Unique("batch") + "@apcs.test");

    private static async Task<string> CreateAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync(
            Route,
            new { name, description = "Fixture batch" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the fixture batch must be created");
        return name;
    }

    private static async Task<Guid> GetIdAsync(HttpClient client, string name)
    {
        using var response = await client.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await response.Content.ReadFromJsonAsync<JsonElement>();
        var match = listed.EnumerateArray()
            .SingleOrDefault(item => item.GetProperty("name").GetString() == name);

        match.ValueKind.Should().NotBe(JsonValueKind.Undefined, "the created batch must be listed");
        return match.GetProperty("id").GetGuid();
    }
}
