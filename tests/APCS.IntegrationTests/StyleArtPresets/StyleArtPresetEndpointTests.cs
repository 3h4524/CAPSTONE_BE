using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using APCS.Application.Features.StyleArtPresets.Common;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Npgsql;

namespace APCS.IntegrationTests.StyleArtPresets;

/// <summary>
/// Covers the art style endpoints end to end against real PostgreSQL, including the ownership and
/// tenant boundaries the service enforces in its query filters.
/// </summary>
/// <remarks>
/// <para>
/// This resource was chosen over <c>Batches</c> because it is the only CRUD surface in the API whose
/// every layer can be reached over HTTP: the multipart endpoints bind an uploaded file through the
/// production <c>ToUploadFileDto</c> mapping into a validator that checks its content type and size,
/// then persist through a repository backed by the real <c>style_art_presets</c> table, complete
/// with the case-insensitive unique index on <c>name</c>. A failure at any layer therefore shows up
/// here as a status code rather than as a mocked expectation.
/// </para>
/// <para>
/// Ownership is the reason this suite is worth having at all. A style is visible to its creator and
/// to every seller when it is a system template, is editable only by its creator, and is refused for
/// a system template with a forbidden answer. Those rules live in the service's
/// <c>AsNoTracking</c> filters and the ownership checks around them, and an isolation test against
/// <see cref="IRepository{TEntity}"/> cannot prove them, because a mock returns whatever the test
/// told it to return.
/// </para>
/// </remarks>
[TestClass]
public sealed class StyleArtPresetEndpointTests
{
    private const string Route = "/api/style-art-presets";
    private const string NotFound = "StyleArtPresets.NotFound";
    private const string NotOwner = "StyleArtPresets.NotOwner";
    private const string DuplicateName = "StyleArtPresets.DuplicateName";
    private const string Validation = "validation.failed";

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
    public Task ResetDatabaseAsync() => DedicatedDatabaseReset.TruncateConfiguredAsync();

    [TestMethod]
    public async Task Create_WhenEveryFieldIsAccepted_ReturnsCreated()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;
        var name = TestAccount.Unique("Neon");

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = name,
                ["Description"] = "Bubbling purple light",
                ["StyleModifiers"] = "neon, glow",
                ["RecommendationsJson"] = "[\"t-shirt\",\"poster\"]"
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await ReadPresetAsync(response);
        created.GetProperty("name").GetString().Should().Be(name);
        created.GetProperty("description").GetString().Should().Be("Bubbling purple light");
        created.GetProperty("styleModifiers").GetString().Should().Be("neon, glow");
        created.GetProperty("isMine").GetBoolean().Should().BeTrue();
        created.GetProperty("isSystemTemplate").GetBoolean().Should().BeFalse();
        created.GetProperty("recommendations").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("t-shirt", "poster");
    }

    [TestMethod]
    public async Task Create_WhenTheNameIsMissing_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Description"] = "No name supplied",
                ["StyleModifiers"] = "modifiers"
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailValidationAsync(HttpStatusCode.BadRequest, "Name");
    }

    [TestMethod]
    public async Task Create_WhenTheDescriptionIsEmpty_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = TestAccount.Unique("NoDescription"),
                ["Description"] = string.Empty,
                ["StyleModifiers"] = "modifiers"
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailValidationAsync(HttpStatusCode.BadRequest, "Description");
    }

    [TestMethod]
    public async Task Create_WhenTheStyleModifiersAreEmpty_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = TestAccount.Unique("NoModifiers"),
                ["Description"] = "Description",
                ["StyleModifiers"] = string.Empty
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailValidationAsync(HttpStatusCode.BadRequest, "StyleModifiers");
    }

    [TestMethod]
    public async Task Create_WhenTheDescriptionExceedsTheMaximumLength_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = TestAccount.Unique("LongDescription"),
                ["Description"] = new string('d', StyleArtPresetRules.MaximumDescriptionLength + 1),
                ["StyleModifiers"] = "modifiers"
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Create_WhenTheStyleModifiersExceedTheMaximumLength_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = TestAccount.Unique("LongModifiers"),
                ["Description"] = "Description",
                ["StyleModifiers"] = new string('m', StyleArtPresetRules.MaximumModifiersLength + 1)
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Create_WhenThereAreMoreRecommendationsThanAllowed_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            ValidFields(TestAccount.Unique("TooManyRecommendations")),
            PreviewFile.OnePixelPng());

        form.Add(
            new StringContent(
                JsonSerializer.Serialize(
                    Enumerable.Range(0, StyleArtPresetRules.MaximumRecommendations + 1).Select(index => $"item-{index}")),
                Encoding.UTF8),
            "RecommendationsJson");

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Create_WhenARecommendationExceedsTheMaximumLength_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        var tooLong = new string('r', StyleArtPresetRules.MaximumRecommendationLength + 1);

        using var form = TestAccount.BuildStylePresetForm(
            ValidFields(TestAccount.Unique("LongRecommendation")),
            PreviewFile.OnePixelPng());

        form.Add(
            new StringContent(JsonSerializer.Serialize(new[] { tooLong }), Encoding.UTF8),
            "RecommendationsJson");

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Create_WhenThePreviewExceedsTheSizeLimit_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        var oversized = new PreviewFile(
            "preview.png",
            "image/png",
            new byte[StyleArtPresetRules.MaximumPreviewBytes + 1]);

        using var form = TestAccount.BuildStylePresetForm(
            ValidFields(TestAccount.Unique("OversizedPreview")),
            oversized);

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Create_WhenTheCallerIsNotSignedIn_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var form = TestAccount.BuildStylePresetForm(
            ValidFields(TestAccount.Unique("Anonymous")),
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Create_WhenTheNameExceedsTheMaximumLength_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = new string('n', StyleArtPresetRules.MaximumNameLength + 1),
                ["Description"] = "Too long a name",
                ["StyleModifiers"] = "modifiers"
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Create_WhenTheNameIsExactlyTheMaximumLength_ReturnsCreated()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;
        var name = new string('n', StyleArtPresetRules.MaximumNameLength);

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = name,
                ["Description"] = "Longest accepted name",
                ["StyleModifiers"] = "modifiers"
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadPresetAsync(response)).GetProperty("name").GetString().Should().Be(name);
    }

    [TestMethod]
    public async Task Create_WhenThePreviewIsMissing_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = TestAccount.Unique("NoPreview"),
                ["Description"] = "Description",
                ["StyleModifiers"] = "modifiers"
            });

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Create_WhenThePreviewIsNotAnImage_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = TestAccount.Unique("NotAnImage"),
                ["Description"] = "Description",
                ["StyleModifiers"] = "modifiers"
            },
            new PreviewFile("notes.txt", "text/plain", "not an image"u8.ToArray()));

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Create_WhenTheRecommendationsAreNotAJsonArray_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = TestAccount.Unique("BadRecommendations"),
                ["Description"] = "Description",
                ["StyleModifiers"] = "modifiers",
                ["RecommendationsJson"] = "not json"
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Create_WhenTheNameIsAlreadyTaken_ReturnsConflict()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        using var client = account.Client;
        var name = await CreateAsync(client, TestAccount.Unique("Taken"));

        using var second = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = name,
                ["Description"] = "Second style with the same name",
                ["StyleModifiers"] = "modifiers"
            },
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, second);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Conflict, DuplicateName);
    }

    [TestMethod]
    public async Task Create_WhenTheNameIsTakenByAnotherSeller_ReturnsConflict()
    {
        _host.EnsureAvailable();
        var first = await SignInAsync();
        var second = await SignInAsync();
        var name = await CreateAsync(first.Client, TestAccount.Unique("Shared"));

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = name,
                ["Description"] = "Same name, different seller",
                ["StyleModifiers"] = "modifiers"
            },
            PreviewFile.OnePixelPng());

        using var response = await second.Client.PostAsync(Route, form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Conflict, DuplicateName);
    }

    [TestMethod]
    public async Task QuickCreate_WhenOnlyANameIsGiven_ReturnsCreated()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = TestAccount.Unique("Quick");

        using var response = await account.Client.PostAsJsonAsync($"{Route}/quick", new { name });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await ReadPresetAsync(response);
        created.GetProperty("name").GetString().Should().Be(name);
        created.GetProperty("previewImageUrl").ValueKind.Should().Be(JsonValueKind.Null);
        created.GetProperty("recommendations").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task QuickCreate_WhenTheNameIsEmpty_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync($"{Route}/quick", new { name = string.Empty });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task QuickCreate_WhenTheNameExceedsTheMaximumLength_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = new string('q', StyleArtPresetRules.MaximumNameLength + 1);

        using var response = await account.Client.PostAsJsonAsync($"{Route}/quick", new { name });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task QuickCreate_WhenTheNameIsAlreadyTaken_ReturnsConflict()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = TestAccount.Unique("QuickDuplicate");
        await CreateAsync(account.Client, name);

        using var response = await account.Client.PostAsJsonAsync($"{Route}/quick", new { name });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Conflict, DuplicateName);
    }

    [TestMethod]
    public async Task QuickCreate_WhenTheNameIsAlreadyTakenByAnotherSeller_ReturnsConflict()
    {
        _host.EnsureAvailable();
        var first = await SignInAsync();
        var second = await SignInAsync();
        var name = TestAccount.Unique("QuickShared");
        await CreateAsync(first.Client, name);

        using var response = await second.Client.PostAsJsonAsync($"{Route}/quick", new { name });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Conflict, DuplicateName);
    }

    [TestMethod]
    public async Task QuickCreate_WhenTheCallerIsNotSignedIn_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(
            $"{Route}/quick",
            new { name = TestAccount.Unique("AnonymousQuick") });

        await response.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task List_OmitsAnInactiveStyle()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var inactiveName = await InsertPresetAsync(
            TestAccount.Unique("Inactive"),
            usageCount: 0,
            isSystemTemplate: false,
            userId: account.UserId,
            isActive: false);

        using var response = await account.Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await response.Content.ReadFromJsonAsync<JsonElement>();
        listed.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should().NotContain(inactiveName);
    }

    [TestMethod]
    public async Task List_WhenTheCallerIsNotSignedIn_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.GetAsync(Route);

        await response.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task List_ReturnsTheCallerOwnStylesAndTheSystemStyles()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var ownName = await CreateAsync(account.Client, TestAccount.Unique("Own"));
        var systemName = await InsertSystemPresetAsync(TestAccount.Unique("System"), usageCount: 40);

        using var response = await account.Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await response.Content.ReadFromJsonAsync<JsonElement>();
        listed.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should().Contain([ownName, systemName]);

        var system = listed.EnumerateArray().Single(item => item.GetProperty("name").GetString() == systemName);
        system.GetProperty("isSystemTemplate").GetBoolean().Should().BeTrue();
        system.GetProperty("isMine").GetBoolean().Should().BeFalse();
    }

    [TestMethod]
    public async Task List_OrdersTheMostUsedStyleFirst()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var popular = await InsertSystemPresetAsync(TestAccount.Unique("Popular"), usageCount: 90);
        await InsertSystemPresetAsync(TestAccount.Unique("Ignored"), usageCount: 1);

        using var response = await account.Client.GetAsync(Route);

        var listed = await response.Content.ReadFromJsonAsync<JsonElement>();
        var names = listed.EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray();

        names[0].Should().Be(popular);
    }

    [TestMethod]
    public async Task List_DoesNotLeakAnotherSellersStyles()
    {
        _host.EnsureAvailable();
        var owner = await SignInAsync();
        var stranger = await SignInAsync();
        var foreignName = await CreateAsync(owner.Client, TestAccount.Unique("Foreign"));

        using var response = await stranger.Client.GetAsync(Route);

        var listed = await response.Content.ReadFromJsonAsync<JsonElement>();
        listed.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should().NotContain(foreignName);
    }

    [TestMethod]
    public async Task Get_WhenTheIdIsValid_ReturnsTheStyle()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Readable"));

        using var response = await account.Client.GetAsync($"{Route}/{await GetOwnedIdAsync(account.Client, name)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPresetAsync(response)).GetProperty("name").GetString().Should().Be(name);
    }

    [TestMethod]
    public async Task Get_WhenTheIdDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync($"{Route}/{Guid.NewGuid()}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, NotFound);
    }

    [TestMethod]
    public async Task Get_WhenAnotherSellerOwnsTheStyle_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var owner = await SignInAsync();
        var stranger = await SignInAsync();
        var name = await CreateAsync(owner.Client, TestAccount.Unique("Hidden"));
        var foreignId = await GetOwnedIdAsync(owner.Client, name);

        using var response = await stranger.Client.GetAsync($"{Route}/{foreignId}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, NotFound);
    }

    [TestMethod]
    public async Task Get_WhenTheIdIsNotAGuid_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync($"{Route}/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("StyleArtPresets.NotFound");
    }

    [TestMethod]
    public async Task Get_WhenTheStyleIsInactive_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var inactiveName = await InsertPresetAsync(
            TestAccount.Unique("Inactive"),
            usageCount: 0,
            isSystemTemplate: false,
            userId: account.UserId,
            isActive: false);
        var inactiveId = await GetSystemPresetIdAsync(inactiveName);

        using var response = await account.Client.GetAsync($"{Route}/{inactiveId}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, NotFound);
    }

    [TestMethod]
    public async Task Get_WhenTheCallerIsNotSignedIn_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.GetAsync($"{Route}/{Guid.NewGuid()}");

        await response.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Get_WhenTheCallerIsNotSignedInAndTheIdIsNotAGuid_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.GetAsync($"{Route}/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Update_WhenEveryFieldIsAccepted_ReturnsOk()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Before"));
        var id = await GetOwnedIdAsync(account.Client, name);
        var renamed = TestAccount.Unique("After");

        using var form = TestAccount.BuildStylePresetForm(
            new Dictionary<string, string>
            {
                ["Name"] = renamed,
                ["Description"] = "Rewritten description",
                ["StyleModifiers"] = "rewritten modifiers",
                ["RecommendationsJson"] = "[\"mug\"]"
            });

        using var response = await account.Client.PutAsync($"{Route}/{id}", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReadPresetAsync(response);
        updated.GetProperty("name").GetString().Should().Be(renamed);
        updated.GetProperty("description").GetString().Should().Be("Rewritten description");
        updated.GetProperty("styleModifiers").GetString().Should().Be("rewritten modifiers");

        using var reread = await account.Client.GetAsync($"{Route}/{id}");
        (await ReadPresetAsync(reread)).GetProperty("name").GetString().Should().Be(renamed);
    }

    [TestMethod]
    public async Task Update_WhenTheIdDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var form = TestAccount.BuildStylePresetForm(ValidFields(TestAccount.Unique("Missing")));
        using var response = await account.Client.PutAsync($"{Route}/{Guid.NewGuid()}", form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, NotFound);
    }

    [TestMethod]
    public async Task Update_WhenARequiredFieldIsEmpty_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Guarded"));
        var id = await GetOwnedIdAsync(account.Client, name);

        var fields = ValidFields(TestAccount.Unique("Still"));
        fields["Name"] = string.Empty;

        using var form = TestAccount.BuildStylePresetForm(fields);
        using var response = await account.Client.PutAsync($"{Route}/{id}", form);

        await response.ShouldFailValidationAsync(HttpStatusCode.BadRequest, "Name");
    }

    [TestMethod]
    public async Task Update_WhenTheNameExceedsTheMaximumLength_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Bounded"));
        var id = await GetOwnedIdAsync(account.Client, name);

        var fields = ValidFields(new string('u', StyleArtPresetRules.MaximumNameLength + 1));

        using var form = TestAccount.BuildStylePresetForm(fields);
        using var response = await account.Client.PutAsync($"{Route}/{id}", form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);

        using var reread = await account.Client.GetAsync($"{Route}/{id}");
        (await ReadPresetAsync(reread)).GetProperty("name").GetString().Should().Be(name);
    }

    [TestMethod]
    public async Task Update_WhenAReplacementPreviewIsUploaded_ReturnsTheStoredPreviewUrl()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("ReplacePreview"));
        var id = await GetOwnedIdAsync(account.Client, name);

        using var form = TestAccount.BuildStylePresetForm(
            ValidFields(name),
            PreviewFile.OnePixelPng("replacement.png"));

        using var response = await account.Client.PutAsync($"{Route}/{id}", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPresetAsync(response)).GetProperty("previewImageUrl").ValueKind.Should().Be(JsonValueKind.String);
    }

    [TestMethod]
    public async Task Update_WhenThePreviewIsDeletedWithoutAReplacement_ClearsThePreviewUrl()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("DeletePreview"));
        var id = await GetOwnedIdAsync(account.Client, name);

        var fields = ValidFields(name);
        fields["DeletePreview"] = "true";

        using var form = TestAccount.BuildStylePresetForm(fields);
        using var response = await account.Client.PutAsync($"{Route}/{id}", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPresetAsync(response)).GetProperty("previewImageUrl").ValueKind.Should().Be(JsonValueKind.Null);

        using var reread = await account.Client.GetAsync($"{Route}/{id}");
        (await ReadPresetAsync(reread)).GetProperty("previewImageUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [TestMethod]
    public async Task Update_WhenTheStyleIsInactive_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var inactiveName = await InsertPresetAsync(
            TestAccount.Unique("InactiveUpdate"),
            usageCount: 0,
            isSystemTemplate: false,
            userId: account.UserId,
            isActive: false);
        var inactiveId = await GetSystemPresetIdAsync(inactiveName);

        using var form = TestAccount.BuildStylePresetForm(ValidFields(TestAccount.Unique("Resurrect")));
        using var response = await account.Client.PutAsync($"{Route}/{inactiveId}", form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, NotFound);
    }

    [TestMethod]
    public async Task Update_WhenTheCallerIsNotSignedIn_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var form = TestAccount.BuildStylePresetForm(ValidFields(TestAccount.Unique("AnonymousUpdate")));
        using var response = await client.PutAsync($"{Route}/{Guid.NewGuid()}", form);

        await response.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Update_WhenBothANewPreviewAndADeletionAreRequested_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("EitherOr"));
        var id = await GetOwnedIdAsync(account.Client, name);

        var fields = ValidFields(TestAccount.Unique("EitherOr"));
        fields["DeletePreview"] = "true";

        using var form = TestAccount.BuildStylePresetForm(fields, PreviewFile.OnePixelPng());
        using var response = await account.Client.PutAsync($"{Route}/{id}", form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, Validation);
    }

    [TestMethod]
    public async Task Update_WhenTheStyleIsASystemPreset_ReturnsForbidden()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var systemName = await InsertSystemPresetAsync(TestAccount.Unique("Locked"), usageCount: 0);
        var systemId = await GetSystemPresetIdAsync(systemName);

        using var form = TestAccount.BuildStylePresetForm(ValidFields(TestAccount.Unique("Hijack")));
        using var response = await account.Client.PutAsync($"{Route}/{systemId}", form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Forbidden, NotOwner);
    }

    [TestMethod]
    public async Task Update_WhenAnotherSellerOwnsTheStyle_ReturnsForbidden()
    {
        _host.EnsureAvailable();
        var owner = await SignInAsync();
        var stranger = await SignInAsync();
        var name = await CreateAsync(owner.Client, TestAccount.Unique("Owned"));
        var foreignId = await GetOwnedIdAsync(owner.Client, name);

        using var form = TestAccount.BuildStylePresetForm(ValidFields(TestAccount.Unique("Stolen")));
        using var response = await stranger.Client.PutAsync($"{Route}/{foreignId}", form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Forbidden, NotOwner);

        using var reread = await owner.Client.GetAsync($"{Route}/{foreignId}");
        (await ReadPresetAsync(reread)).GetProperty("name").GetString().Should().Be(name);
    }

    [TestMethod]
    public async Task Update_WhenTheNewNameIsAlreadyTaken_ReturnsConflict()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var first = await CreateAsync(account.Client, TestAccount.Unique("First"));
        var second = await CreateAsync(account.Client, TestAccount.Unique("Second"));
        var secondId = await GetOwnedIdAsync(account.Client, second);

        using var form = TestAccount.BuildStylePresetForm(ValidFields(first));
        using var response = await account.Client.PutAsync($"{Route}/{secondId}", form);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Conflict, DuplicateName);
    }

    [TestMethod]
    public async Task Delete_WhenTheStyleIsOwned_ReturnsNoContent()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Removable"));
        var id = await GetOwnedIdAsync(account.Client, name);

        using var response = await account.Client.DeleteAsync($"{Route}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var reread = await account.Client.GetAsync($"{Route}/{id}");
        await reread.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, NotFound);
    }

    [TestMethod]
    public async Task Delete_WhenTheIdDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.DeleteAsync($"{Route}/{Guid.NewGuid()}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, NotFound);
    }

    [TestMethod]
    public async Task Delete_WhenCalledTwice_ReturnsNotFoundOnTheSecondCall()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var name = await CreateAsync(account.Client, TestAccount.Unique("Twice"));
        var id = await GetOwnedIdAsync(account.Client, name);

        using var first = await account.Client.DeleteAsync($"{Route}/{id}");
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var second = await account.Client.DeleteAsync($"{Route}/{id}");

        await second.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, NotFound);
    }

    [TestMethod]
    public async Task Delete_WhenAnotherSellerOwnsTheStyle_ReturnsForbiddenAndKeepsTheStyle()
    {
        _host.EnsureAvailable();
        var owner = await SignInAsync();
        var stranger = await SignInAsync();
        var name = await CreateAsync(owner.Client, TestAccount.Unique("Protected"));
        var foreignId = await GetOwnedIdAsync(owner.Client, name);

        using var response = await stranger.Client.DeleteAsync($"{Route}/{foreignId}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Forbidden, NotOwner);

        using var reread = await owner.Client.GetAsync($"{Route}/{foreignId}");
        reread.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task Delete_WhenTheStyleIsASystemPreset_ReturnsForbidden()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var systemName = await InsertSystemPresetAsync(TestAccount.Unique("Undeletable"), usageCount: 0);
        var systemId = await GetSystemPresetIdAsync(systemName);

        using var response = await account.Client.DeleteAsync($"{Route}/{systemId}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Forbidden, NotOwner);

        using var reread = await account.Client.GetAsync($"{Route}/{systemId}");
        reread.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task Delete_WhenTheStyleIsInactive_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var inactiveName = await InsertPresetAsync(
            TestAccount.Unique("InactiveDelete"),
            usageCount: 0,
            isSystemTemplate: false,
            userId: account.UserId,
            isActive: false);
        var inactiveId = await GetSystemPresetIdAsync(inactiveName);

        using var response = await account.Client.DeleteAsync($"{Route}/{inactiveId}");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, NotFound);
    }

    [TestMethod]
    public async Task Delete_WhenTheCallerIsNotSignedIn_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.DeleteAsync($"{Route}/{Guid.NewGuid()}");

        await response.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Delete_WhenTheCallerIsNotSignedInAndTheIdIsNotAGuid_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.DeleteAsync($"{Route}/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<AuthenticatedAccount> SignInAsync() =>
        TestAccount.SignInAsync(_host, TestAccount.Unique($"seller{Guid.NewGuid():N}") + "@apcs.test");

    private static Dictionary<string, string> ValidFields(string name) =>
        new()
        {
            ["Name"] = name,
            ["Description"] = "Description",
            ["StyleModifiers"] = "modifiers"
        };

    private static async Task<string> CreateAsync(HttpClient client, string name)
    {
        using var form = TestAccount.BuildStylePresetForm(
            ValidFields(name),
            PreviewFile.OnePixelPng());

        using var response = await client.PostAsync(Route, form);
        response.StatusCode.Should().Be(HttpStatusCode.Created, "the fixture style must be created");

        return name;
    }

    private static async Task<Guid> GetOwnedIdAsync(HttpClient client, string name)
    {
        using var response = await client.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await response.Content.ReadFromJsonAsync<JsonElement>();
        var match = listed.EnumerateArray()
            .SingleOrDefault(item => item.GetProperty("name").GetString() == name);

        match.ValueKind.Should().NotBe(JsonValueKind.Undefined, "the created style must be listed for its owner");

        return match.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> GetSystemPresetIdAsync(string name)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT id FROM style_art_presets WHERE name = @name",
            connection);

        command.Parameters.AddWithValue("name", name);

        var result = await command.ExecuteScalarAsync();
        result.Should().NotBeNull("the seeded system style must exist");

        return (Guid)result!;
    }

    private static Task<string> InsertSystemPresetAsync(string name, int usageCount) =>
        InsertPresetAsync(name, usageCount, isSystemTemplate: true, userId: null, isActive: true);

    private static async Task<string> InsertPresetAsync(
        string name,
        int usageCount,
        bool isSystemTemplate,
        Guid? userId,
        bool isActive)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO style_art_presets
                (id, name, description, style_modifiers, recommendations, is_system_template, is_active, usage_count, user_id)
            VALUES
                (@id, @name, @description, @style_modifiers, '[]'::jsonb, @is_system_template, @is_active, @usage_count, @user_id)
            """,
            connection);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("description", "A style provided outside the create endpoint");
        command.Parameters.AddWithValue("style_modifiers", "seeded");
        command.Parameters.AddWithValue("is_system_template", isSystemTemplate);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("usage_count", usageCount);
        command.Parameters.AddWithValue("user_id", (object?)userId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();

        return name;
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");

    private static async Task<JsonElement> ReadPresetAsync(HttpResponseMessage response)
    {
        var preset = await response.Content.ReadFromJsonAsync<JsonElement>();
        preset.ValueKind.Should().Be(JsonValueKind.Object);

        return preset;
    }
}