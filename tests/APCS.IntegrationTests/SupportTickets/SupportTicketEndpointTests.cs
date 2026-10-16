using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;

namespace APCS.IntegrationTests.SupportTickets;

/// <summary>
/// Covers the seller support-ticket lifecycle over real HTTP with in-memory file storage.
/// </summary>
/// <remarks>
/// <para>
/// Tickets are the only seller surface that binds multipart uploads through the production
/// mapping into validators that check category, priority, and attachment shape before anything
/// reaches PostgreSQL or the fake storage. Ownership again lives in the service filters: a
/// seller lists and reads only their own tickets, and another seller's identifier answers 404
/// rather than 403 so identifiers cannot be probed.
/// </para>
/// <para>
/// There is no update or delete verb for seller tickets by design; a follow-up is a reply and a
/// resolved ticket accepts a single satisfaction rating. The suite therefore walks create to
/// read to reply to rate as the lifecycle, with validation, authentication, and role-gate
/// branches pinned alongside.
/// </para>
/// </remarks>
[TestClass]
public sealed class SupportTicketEndpointTests
{
    private const string Route = "/api/support-tickets";

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
    public async Task Create_WhenThePayloadIsValid_ReturnsCreated()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var form = BuildCreateForm(TestAccount.Unique("Subject"), "integration", "normal", "The export stalls at half.");
        using var response = await account.Client.PostAsync(Route, form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("subject").GetString().Should().Contain("Subject");
        created.GetProperty("status").GetString().Should().Be("open");
    }

    [TestMethod]
    public async Task List_AfterCreate_ContainsTheTicket()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var subject = TestAccount.Unique("Listed");

        using var form = BuildCreateForm(subject, "other", "low", "A question about billing.");
        using var created = await account.Client.PostAsync(Route, form);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        using var response = await account.Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("subject").GetString())
            .Should().Contain(subject);
    }

    [TestMethod]
    public async Task Get_WhenTheTicketIsOwned_ReturnsTheDetail()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var id = await CreateAsync(account.Client, TestAccount.Unique("Readable"));

        using var response = await account.Client.GetAsync($"{Route}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().Should().Be(id);
    }

    [TestMethod]
    public async Task Get_WhenAnotherSellerOwnsTheTicket_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var owner = await SignInAsync();
        var stranger = await SignInAsync();
        var id = await CreateAsync(owner.Client, TestAccount.Unique("Hidden"));

        using var response = await stranger.Client.GetAsync($"{Route}/{id}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, "support_tickets.not_found");
    }

    [TestMethod]
    public async Task Reply_WhenThePayloadIsValid_ReturnsCreated()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var id = await CreateAsync(account.Client, TestAccount.Unique("FollowUp"));

        using var form = new MultipartFormDataContent { { new StringContent("Still stuck here."), "ReplyText" } };
        using var response = await account.Client.PostAsync($"{Route}/{id}/replies", form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [TestMethod]
    public async Task Reply_WhenBothTextAndAttachmentAreMissing_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var id = await CreateAsync(account.Client, TestAccount.Unique("EmptyReply"));

        using var form = new MultipartFormDataContent { { new StringContent(string.Empty), "ReplyText" } };
        using var response = await account.Client.PostAsync($"{Route}/{id}/replies", form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task Create_WhenTheCategoryIsUnknown_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var form = BuildCreateForm(TestAccount.Unique("Bad"), "not-a-category", "normal", "Description.");
        using var response = await account.Client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task Create_WhenTheSubjectIsMissing_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var form = BuildCreateForm(string.Empty, "other", "low", "Description.");
        using var response = await account.Client.PostAsync(Route, form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task TicketEndpoints_WhenTheCallerIsNotSignedIn_ReturnUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var list = await client.GetAsync(Route);
        await list.ShouldFailAsync(HttpStatusCode.Unauthorized);

        using var form = BuildCreateForm("Anonymous", "other", "low", "Description.");
        using var create = await client.PostAsync(Route, form);
        await create.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task TicketEndpoints_WhenTheTokenCarriesTheAdminRole_ReturnForbidden()
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([TestAuth.AdminRole]);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var list = await client.GetAsync(Route);
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var form = BuildCreateForm("Admin", "other", "low", "Description.");
        using var create = await client.PostAsync(Route, form);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Task<AuthenticatedAccount> SignInAsync() =>
        TestAccount.SignInAsync(_host, TestAccount.Unique("ticket") + "@apcs.test");

    private static MultipartFormDataContent BuildCreateForm(string subject, string category, string priority, string description)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(subject), "Subject" },
            { new StringContent(category), "Category" },
            { new StringContent(priority), "Priority" },
            { new StringContent(description), "Description" }
        };

        return form;
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string subject)
    {
        using var form = BuildCreateForm(subject, "other", "low", "A seller question.");
        using var response = await client.PostAsync(Route, form);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the fixture ticket must be created");

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }
}
