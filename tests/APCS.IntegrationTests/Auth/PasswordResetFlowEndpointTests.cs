using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Application.Abstractions.Email;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using APCS.IntegrationTests.TestSupport.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace APCS.IntegrationTests.Auth;

/// <summary>
/// Walks the forgot-password to reset-password lifecycle over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// The reset link is the only place where a password change is authorized without a session,
/// and its guarantees live in three different layers: the forgot-password use case refuses to
/// disclose whether an address exists, the email body is the sole carrier of the raw token while
/// only its hash is persisted, and the auth token row is what makes the link single-use and
/// expiring. A mocked test would prove only the mock, so every step here goes through the public
/// endpoints against the real PostgreSQL the host runs on.
/// </para>
/// <para>
/// Expiry is forced by backdating the persisted row rather than by waiting out the ten-minute
/// lifetime, because the lifetime is enforced by a comparison in the use case, not by the mail
/// transport. The row update therefore exercises the same branch a naturally expired link would.
/// </para>
/// </remarks>
[TestClass]
public sealed class PasswordResetFlowEndpointTests
{
    private const string Password = "Passw0rdTest1!";
    private const string NewPassword = "NewPassw0rdTest2!";
    private const string WeakPassword = "alllowercase1";
    private const string RegisterRoute = "/api/auth/register";
    private const string VerifyRoute = "/api/auth/verify-email";
    private const string LoginRoute = "/api/auth/login";
    private const string ForgotRoute = "/api/auth/forgot-password";
    private const string ResetRoute = "/api/auth/reset-password";
    private const string TokenQueryKey = "token=";

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
    public async Task ForgotPassword_WhenTheAddressIsRegistered_AnswersNoContent()
    {
        _host.EnsureAvailable();
        var email = await RegisterAndVerifyAsync();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ForgotRoute, new { email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task ForgotPassword_WhenTheAddressIsRegistered_EmailsAResetLink()
    {
        _host.EnsureAvailable();
        var email = await RegisterAndVerifyAsync();
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ForgotRoute, new { email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var message = inbox.Messages
            .Skip(before)
            .Single(recorded => string.Equals(recorded.Recipient, email, StringComparison.OrdinalIgnoreCase));
        message.Body.Should().Contain("/reset-password?token=");
    }

    [TestMethod]
    public async Task ForgotPassword_WhenTheAddressIsUnknown_AnswersNoContent()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(
            ForgotRoute,
            new { email = TestAccount.Unique("ghost") + "@apcs.test" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task ForgotPassword_WhenTheAddressIsUnknown_SendsNoEmail()
    {
        _host.EnsureAvailable();
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;
        var unknown = TestAccount.Unique("ghost") + "@apcs.test";

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ForgotRoute, new { email = unknown });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        inbox.Messages.Skip(before).Should().BeEmpty("an unknown address must not produce a reset email");
    }

    [TestMethod]
    public async Task ForgotPassword_WhenTheEmailIsMalformed_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(ForgotRoute, new { email = "not-an-email" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task ForgotPassword_WhenTheAccountIsNotVerified_SendsNoResetEmail()
    {
        _host.EnsureAvailable();
        var email = await RegisterAsync();
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ForgotRoute, new { email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        inbox.Messages
            .Skip(before)
            .Where(recorded => string.Equals(recorded.Recipient, email, StringComparison.OrdinalIgnoreCase)
                && recorded.Body.Contains("/reset-password?token=", StringComparison.Ordinal))
            .Should().BeEmpty("an unverified account must not receive a reset link");
    }

    [TestMethod]
    public async Task ResetPassword_WhenTheTokenIsValid_RedeemsAndAllowsSignInWithNewPassword()
    {
        _host.EnsureAvailable();
        var email = await RegisterAndVerifyAsync();
        await RequestResetAsync(email);
        var token = ReadLatestResetToken(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var reset = await client.PostAsJsonAsync(ResetRoute, new { token, newPassword = NewPassword });

        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var loginWithNew = await client.PostAsJsonAsync(LoginRoute, new { email, password = NewPassword });
        loginWithNew.StatusCode.Should().Be(HttpStatusCode.OK);

        using var anonymous = TestAccount.CreateAnonymousClient(_host);
        using var loginWithOld = await anonymous.PostAsJsonAsync(LoginRoute, new { email, password = Password });
        await loginWithOld.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    [TestMethod]
    public async Task ResetPassword_WhenTheTokenIsReused_IsRefused()
    {
        _host.EnsureAvailable();
        var email = await RegisterAndVerifyAsync();
        await RequestResetAsync(email);
        var token = ReadLatestResetToken(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var first = await client.PostAsJsonAsync(ResetRoute, new { token, newPassword = NewPassword });
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var second = await client.PostAsJsonAsync(ResetRoute, new { token, newPassword = "AnotherPass1!" });
        await second.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.PasswordResetTokenInvalid);
    }

    [TestMethod]
    public async Task ResetPassword_WhenTheTokenIsExpired_IsRefused()
    {
        _host.EnsureAvailable();
        var email = await RegisterAndVerifyAsync();
        await RequestResetAsync(email);
        var token = ReadLatestResetToken(email);
        await ExpireResetTokensAsync(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ResetRoute, new { token, newPassword = NewPassword });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.PasswordResetTokenExpired);
    }

    [TestMethod]
    public async Task ResetPassword_WhenTheTokenIsUnknown_IsRefused()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(
            ResetRoute,
            new { token = "not-a-real-reset-token", newPassword = NewPassword });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.PasswordResetTokenInvalid);
    }

    [TestMethod]
    public async Task ResetPassword_WhenTheNewPasswordIsWeak_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var email = await RegisterAndVerifyAsync();
        await RequestResetAsync(email);
        var token = ReadLatestResetToken(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ResetRoute, new { token, newPassword = WeakPassword });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task ResetPassword_WhenTheTokenIsEmpty_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(ResetRoute, new { token = string.Empty, newPassword = NewPassword });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task ForgotPassword_WhenRequestedTwice_RevokesTheFirstLink()
    {
        _host.EnsureAvailable();
        var email = await RegisterAndVerifyAsync();
        await RequestResetAsync(email);
        var firstToken = ReadLatestResetToken(email);

        await RequestResetAsync(email);
        var secondToken = ReadLatestResetToken(email);

        secondToken.Should().NotBe(firstToken, "a replacement request must issue a fresh token");

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var stale = await client.PostAsJsonAsync(ResetRoute, new { token = firstToken, newPassword = NewPassword });

        await stale.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.PasswordResetTokenInvalid);
    }

    private static string NewEmail() => TestAccount.Unique("reset") + "@apcs.test";

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
            new { email, password = Password, fullName = "Reset Seller" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return email;
    }

    private static async Task<string> RegisterAndVerifyAsync()
    {
        var email = await RegisterAsync();
        var verificationToken = TestAccount.ReadLatestVerificationToken(ReadInbox(), 0, email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var verification = await client.PostAsJsonAsync(VerifyRoute, new { token = verificationToken });
        verification.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return email;
    }

    private static async Task RequestResetAsync(string email)
    {
        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ForgotRoute, new { email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static string ReadLatestResetToken(string email)
    {
        var inbox = ReadInbox();
        var message = inbox.Messages
            .LastOrDefault(recorded => string.Equals(recorded.Recipient, email, StringComparison.OrdinalIgnoreCase)
                && recorded.Body.Contains("/reset-password?token=", StringComparison.Ordinal));

        message.Should().NotBeNull("the host must email a reset link for a verified address");

        var separator = message!.Body.LastIndexOf(TokenQueryKey, StringComparison.Ordinal);
        separator.Should().BeGreaterThanOrEqualTo(0);

        return Uri.UnescapeDataString(message.Body[(separator + TokenQueryKey.Length)..].Trim());
    }

    private static async Task ExpireResetTokensAsync(string email)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            UPDATE auth_tokens
            SET expires_at = now() - interval '1 hour'
            WHERE user_id = (SELECT id FROM users WHERE email = @email)
              AND token_type = 'password_reset';
            """,
            connection);

        command.Parameters.AddWithValue("email", email);
        (await command.ExecuteNonQueryAsync()).Should().BeGreaterThan(0, "a reset token must exist to expire");
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");
}
