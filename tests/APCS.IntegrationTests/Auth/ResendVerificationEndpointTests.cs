using System.Net;
using System.Net.Http.Json;
using APCS.Application.Abstractions.Email;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using APCS.IntegrationTests.TestSupport.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace APCS.IntegrationTests.Auth;

/// <summary>
/// Covers the resend-verification endpoint and the expiry branch of verify-email over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Resend exists because the first verification link can be lost or expire, and its security
/// property is revocation: the outstanding link must stop working once its replacement is issued,
/// otherwise two live links would exist for one account. The test therefore redeems the flow in
/// order, proving the old token is refused and the new one activates the account.
/// </para>
/// <para>
/// Expiry is forced by backdating the persisted row rather than by waiting out the twenty-four
/// hour lifetime, because the lifetime is enforced by a comparison in the use case. Already
/// verified and unknown addresses answer identically to a genuine resend so the endpoint cannot
/// be used to discover accounts.
/// </para>
/// </remarks>
[TestClass]
public sealed class ResendVerificationEndpointTests
{
    private const string Password = "Passw0rdTest1!";
    private const string RegisterRoute = "/api/auth/register";
    private const string VerifyRoute = "/api/auth/verify-email";
    private const string ResendRoute = "/api/auth/resend-verification";
    private const string LoginRoute = "/api/auth/login";

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
    public async Task ResendVerification_WhenTheAccountIsPending_AnswersNoContent()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ResendRoute, new { email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task ResendVerification_WhenTheAccountIsPending_EmailsAReplacementLink()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ResendRoute, new { email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        inbox.Messages
            .Skip(before)
            .Count(recorded => string.Equals(recorded.Recipient, email, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "a pending account must receive exactly one replacement link");
    }

    [TestMethod]
    public async Task ResendVerification_WhenTheAccountIsPending_RevokesTheFirstLink()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();
        var firstToken = TestAccount.ReadLatestVerificationToken(ReadInbox(), 0, email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var resend = await client.PostAsJsonAsync(ResendRoute, new { email });
        resend.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var secondToken = TestAccount.ReadLatestVerificationToken(ReadInbox(), 0, email);
        secondToken.Should().NotBe(firstToken, "a resend must issue a fresh token");

        using var stale = await client.PostAsJsonAsync(VerifyRoute, new { token = firstToken });
        await stale.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.VerificationTokenInvalid);

        using var fresh = await client.PostAsJsonAsync(VerifyRoute, new { token = secondToken });
        fresh.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task ResendVerification_WhenTheReplacementIsRedeemed_ActivatesTheAccount()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var resend = await client.PostAsJsonAsync(ResendRoute, new { email });
        resend.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var replacement = TestAccount.ReadLatestVerificationToken(ReadInbox(), 0, email);
        using var verification = await client.PostAsJsonAsync(VerifyRoute, new { token = replacement });
        verification.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var login = await client.PostAsJsonAsync(LoginRoute, new { email, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK, "a verified account must be able to sign in");
    }

    [TestMethod]
    public async Task ResendVerification_WhenTheAccountIsAlreadyVerified_AnswersNoContentWithoutNewEmail()
    {
        _host.EnsureAvailable();
        var email = await RegisterAndVerifyAsync();
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ResendRoute, new { email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        inbox.Messages.Skip(before).Should().BeEmpty("a verified account needs no replacement link");
    }

    [TestMethod]
    public async Task ResendVerification_WhenTheAddressIsUnknown_AnswersNoContentWithoutEmail()
    {
        _host.EnsureAvailable();
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;
        var unknown = TestAccount.Unique("ghost") + "@apcs.test";

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ResendRoute, new { email = unknown });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        inbox.Messages.Skip(before).Should().BeEmpty("an unknown address must not produce an email");
    }

    [TestMethod]
    public async Task ResendVerification_WhenTheEmailIsMalformed_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(ResendRoute, new { email = "not-an-email" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task VerifyEmail_WhenTheTokenIsExpired_IsRefused()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();
        var token = TestAccount.ReadLatestVerificationToken(ReadInbox(), 0, email);
        await ExpireVerificationTokensAsync(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(VerifyRoute, new { token });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.VerificationTokenExpired);
    }

    [TestMethod]
    public async Task VerifyEmail_WhenTheTokenIsExpired_KeepsTheAccountPending()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();
        var token = TestAccount.ReadLatestVerificationToken(ReadInbox(), 0, email);
        await ExpireVerificationTokensAsync(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var verification = await client.PostAsJsonAsync(VerifyRoute, new { token });
        verification.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var login = await client.PostAsJsonAsync(LoginRoute, new { email, password = Password });
        await login.ShouldFailWithCodeAsync(HttpStatusCode.Forbidden, ErrorCodes.EmailNotVerified);
    }

    private static string NewEmail() => TestAccount.Unique("resend") + "@apcs.test";

    private static FakeEmailService ReadInbox()
    {
        var inbox = _host.Factory.Services.GetService<IEmailService>();

        inbox.Should().BeOfType<FakeEmailService>("the host must send through the recording fake");
        return (FakeEmailService)inbox!;
    }

    private static async Task<string> RegisterAsync()
    {
        var email = NewEmail();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email, password = Password, fullName = "Resend Seller" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return email;
    }

    private static async Task<string> RegisterAndVerifyAsync()
    {
        var email = await RegisterAsync();
        var token = TestAccount.ReadLatestVerificationToken(ReadInbox(), 0, email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var verification = await client.PostAsJsonAsync(VerifyRoute, new { token });
        verification.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return email;
    }

    private static async Task ExpireVerificationTokensAsync(string email)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            UPDATE auth_tokens
            SET expires_at = now() - interval '1 hour'
            WHERE user_id = (SELECT id FROM users WHERE email = @email)
              AND token_type = 'email_verification';
            """,
            connection);

        command.Parameters.AddWithValue("email", email);
        (await command.ExecuteNonQueryAsync()).Should().BeGreaterThan(0, "a verification token must exist to expire");
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");
}
