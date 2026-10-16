using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Application.Abstractions.Email;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using APCS.IntegrationTests.TestSupport.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace APCS.IntegrationTests.Auth;

/// <summary>
/// Walks the whole account lifecycle over real HTTP: register, verify, sign in, refresh, sign out.
/// </summary>
/// <remarks>
/// <para>
/// These endpoints are the only place where the security properties of the product live, and each
/// one is enforced in a different layer: the registration validator rejects a weak password, the
/// use case refuses an address it already knows, the email filter is what delivers the activation
/// token, the token table is what makes a link single-use, and the cookie contract is what carries
/// a session. A test that mocked any of those would only prove the mock was configured correctly,
/// so every step here goes through the public endpoints and the real PostgreSQL and Redis the host
/// is configured against.
/// </para>
/// <para>
/// The single-use guarantee on the verification link and the rotation guarantee on the refresh
/// token are the two properties worth protecting, because both are enforced by a write against a
/// row rather than by a comparison in memory. The refresh race is therefore driven from several
/// independent clients replaying one captured token, which is the same shape as a browser firing
/// two refreshes from two tabs.
/// </para>
/// </remarks>
[TestClass]
public sealed class AuthFlowEndpointTests
{
    private const string Password = "Passw0rdTest1!";
    private const string WeakPassword = "alllowercase1";
    private const string RegisterRoute = "/api/auth/register";
    private const string VerifyRoute = "/api/auth/verify-email";
    private const string LoginRoute = "/api/auth/login";
    private const string RefreshRoute = "/api/auth/refresh";
    private const string LogoutRoute = "/api/auth/logout";
    private const string MeRoute = "/api/auth/me";

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
    public async Task Register_WhenTheAddressIsNew_AnswersTheAccountWithoutASession()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);
        var email = NewEmail();

        using var response = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email, password = Password, fullName = "Fresh Seller" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Set-Cookie").Should().BeFalse("registration issues no session");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("userId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("email").GetString().Should().Be(email);
        body.GetProperty("requiresEmailVerification").GetBoolean().Should().BeTrue();
    }

    [TestMethod]
    public async Task Register_WhenTheAddressIsNew_LeavesTheAccountPendingVerification()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();

        var state = await ReadAccountStateAsync(email);

        state.Status.Should().Be("pending_verification");
        state.EmailVerified.Should().BeFalse();
        state.EmailVerifiedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task Register_WhenTheAddressIsNew_EmailsAVerificationLink()
    {
        _host.EnsureAvailable();
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;
        var email = await RegisterAsync();

        var message = inbox.Messages
            .Skip(before)
            .Single(recorded => string.Equals(recorded.Recipient, email, StringComparison.OrdinalIgnoreCase));

        message.Body.Should().Contain("/verify-email?token=");
    }

    [TestMethod]
    public async Task Register_WhenTheAddressIsAlreadyRegistered_ReturnsConflict()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email, password = Password, fullName = "Impostor" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Conflict, ErrorCodes.EmailAlreadyExists);
    }

    [TestMethod]
    public async Task Register_WhenTheAddressDiffersOnlyInCase_ReturnsConflict()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email = email.ToUpperInvariant(), password = Password, fullName = "Impostor" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Conflict, ErrorCodes.EmailAlreadyExists);
    }

    [TestMethod]
    public async Task Register_WhenTheEmailIsMalformed_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email = "not-an-email", password = Password, fullName = "Malformed" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task Register_WhenThePasswordHasNoUppercase_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email = NewEmail(), password = WeakPassword, fullName = "Weak Password" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task Register_WhenThePasswordIsTooShort_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email = NewEmail(), password = "Ab1!", fullName = "Short Password" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task Login_BeforeTheEmailIsVerified_IsRefused()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(LoginRoute, new { email, password = Password });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Forbidden, ErrorCodes.EmailNotVerified);
        response.Headers.Contains("Set-Cookie").Should().BeFalse("a refused sign-in issues no session");
    }

    [TestMethod]
    public async Task VerifyEmail_WhenTheTokenIsUnknown_IsRefused()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(
            VerifyRoute,
            new { token = "not-a-real-verification-token" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.VerificationTokenInvalid);
    }

    [TestMethod]
    public async Task VerifyEmail_WhenTheTokenIsEmpty_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(VerifyRoute, new { token = string.Empty });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task VerifyEmail_WhenTheTokenIsRedeemed_IsSpentAfterwards()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();
        var token = ReadLatestVerificationToken(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var first = await client.PostAsJsonAsync(VerifyRoute, new { token });

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAccountStateAsync(email)).EmailVerified.Should().BeTrue();

        using var second = await client.PostAsJsonAsync(VerifyRoute, new { token });

        await second.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.VerificationTokenInvalid);
    }

    [TestMethod]
    public async Task Login_AfterVerification_IssuesBothSessionCookies()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            LoginRoute,
            new { email = account.Email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var cookies = ReadSetCookies(response);
        cookies.Should().ContainKey(AuthConstants.AccessTokenCookieName);
        cookies.Should().ContainKey(AuthConstants.RefreshTokenCookieName);
        cookies[AuthConstants.AccessTokenCookieName].Should().NotBeNullOrWhiteSpace();
        cookies[AuthConstants.RefreshTokenCookieName].Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task Login_AfterVerification_AnswersTheAuthenticatedUser()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            LoginRoute,
            new { email = account.Email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("user").GetProperty("email").GetString().Should().Be(account.Email);
        body.GetProperty("requiresTwoFactor").GetBoolean().Should().BeFalse();
        body.TryGetProperty("accessToken", out _).Should().BeFalse("the access token travels in a cookie only");
    }

    [TestMethod]
    public async Task Me_WithASession_AnswersTheSignedInAccount()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync(MeRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("email").GetString().Should().Be(account.Email);
        body.GetProperty("roles")
            .EnumerateArray()
            .Select(role => role.GetString())
            .Should().Contain(AuthConstants.UserRole);
    }

    [TestMethod]
    public async Task Me_WithoutASession_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.GetAsync(MeRoute);

        await response.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Refresh_WithTheIssuedToken_ReturnsANewTokenPair()
    {
        _host.EnsureAvailable();
        var (account, issued) = await SignInWithRefreshTokenAsync();

        using var response = await account.Client.PostAsync(RefreshRoute, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotated = ReadSetCookies(response);
        rotated.Should().ContainKey(AuthConstants.RefreshTokenCookieName);
        rotated[AuthConstants.RefreshTokenCookieName].Should().NotBe(issued, "a refresh must rotate the token");

        using var me = await account.Client.GetAsync(MeRoute);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task Refresh_WithoutARefreshTokenCookie_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsync(RefreshRoute, content: null);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.RefreshTokenMissing);
    }

    [TestMethod]
    public async Task Refresh_WhenTheRotatedTokenIsReplayed_IsRefused()
    {
        _host.EnsureAvailable();
        var (account, issued) = await SignInWithRefreshTokenAsync();

        using var rotation = await account.Client.PostAsync(RefreshRoute, content: null);
        rotation.StatusCode.Should().Be(HttpStatusCode.OK);

        using var replay = await RefreshWithTokenAsync(issued);

        await replay.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.RefreshTokenReused);
    }

    [TestMethod]
    public async Task Refresh_WhenTheTokenIsUnknown_IsRefused()
    {
        _host.EnsureAvailable();

        using var response = await RefreshWithTokenAsync("not-a-real-refresh-token");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.RefreshTokenInvalid);
    }

    [TestMethod]
    public async Task Refresh_WhenFourRequestsRaceTheSameToken_OnlyOneSucceeds()
    {
        _host.EnsureAvailable();
        var (account, issued) = await SignInWithRefreshTokenAsync();
        var racers = Enumerable.Range(0, 4).Select(_ => RefreshWithTokenAsync(issued)).ToArray();

        var responses = await Task.WhenAll(racers);

        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.OK)
                .Should().Be(1, "the token is single-use, so exactly one rotation may win");
            responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized)
                .Should().Be(3, "every loser must be told the token is no longer usable");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        using var me = await account.Client.GetAsync(MeRoute);
        me.StatusCode.Should().Be(HttpStatusCode.OK, "the winning rotation keeps the session usable");
    }

    [TestMethod]
    public async Task Logout_WithASession_AnswersNoContent()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsync(LogoutRoute, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task Logout_RevokesTheRefreshTokenSoAReplayIsRefused()
    {
        _host.EnsureAvailable();
        var (account, issued) = await SignInWithRefreshTokenAsync();

        using var logout = await account.Client.PostAsync(LogoutRoute, content: null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var replay = await RefreshWithTokenAsync(issued);

        await replay.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.RefreshTokenReused);
    }

    [TestMethod]
    public async Task Logout_WhenTheCallerIsNotSignedIn_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsync(LogoutRoute, content: null);

        await response.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    private static string NewEmail() => TestAccount.Unique($"flower{Guid.NewGuid():N}") + "@apcs.test";

    private static FakeEmailService ReadInbox()
    {
        var inbox = _host.Factory.Services.GetService<IEmailService>();

        inbox.Should().BeOfType<FakeEmailService>("the host must send through the recording fake");
        return (FakeEmailService)inbox!;
    }

    private static async Task<string> RegisterAsync(string? email = null)
    {
        var address = email ?? NewEmail();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email = address, password = Password, fullName = "Fresh Seller" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the registration request must be accepted");

        return address;
    }

    private static string ReadLatestVerificationToken(string email)
    {
        var inbox = ReadInbox();

        var message = inbox.Messages
            .LastOrDefault(recorded => string.Equals(recorded.Recipient, email, StringComparison.OrdinalIgnoreCase));

        message.Should().NotBeNull("the host must email a verification link for every registration");

        return TestAccount.ReadLatestVerificationToken(inbox, 0, email);
    }

    private static async Task<AuthenticatedAccount> SignInAsync()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var token = ReadLatestVerificationToken(email);

        var client = TestAccount.CreateAnonymousClient(_host);
        using var verification = await client.PostAsJsonAsync(VerifyRoute, new { token = token });
        verification.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var login = await client.PostAsJsonAsync(LoginRoute, new { email, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var userId = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("user")
            .GetProperty("id")
            .GetGuid();

        return new AuthenticatedAccount(email, Password, userId, client, ReadInbox());
    }

    private static async Task<(AuthenticatedAccount Account, string RefreshToken)> SignInWithRefreshTokenAsync()
    {
        var account = await SignInAsync();

        using var login = await account.Client.PostAsJsonAsync(
            LoginRoute,
            new { email = account.Email, password = Password });

        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var cookies = ReadSetCookies(login);
        cookies.Should().ContainKey(AuthConstants.RefreshTokenCookieName);

        var refreshToken = cookies[AuthConstants.RefreshTokenCookieName];
        refreshToken.Should().NotBeNullOrWhiteSpace();

        return (account, refreshToken!);
    }

    private static async Task<HttpResponseMessage> RefreshWithTokenAsync(string refreshToken)
    {
        using var client = _host.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = TestAccount.SecureOrigin,
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshRoute);
        request.Headers.Add("Cookie", $"{AuthConstants.RefreshTokenCookieName}={refreshToken}");

        return await client.SendAsync(request);
    }

    private static Dictionary<string, string?> ReadSetCookies(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return cookies;
        }

        foreach (var header in headers)
        {
            var pair = header.Split(';', StringSplitOptions.TrimEntries)[0];
            var separator = pair.IndexOf('=');

            if (separator > 0)
            {
                cookies[Uri.UnescapeDataString(pair[..separator])] = pair[(separator + 1)..];
            }
        }

        return cookies;
    }

    private static async Task<(string Status, bool EmailVerified, DateTimeOffset? EmailVerifiedAt)>
        ReadAccountStateAsync(string email)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT account_status, email_verified, email_verified_at FROM users WHERE email = @email",
            connection);

        command.Parameters.AddWithValue("email", email);

        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue("the registration must have persisted the account");

        return (
            reader.GetString(0),
            reader.GetBoolean(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");
}