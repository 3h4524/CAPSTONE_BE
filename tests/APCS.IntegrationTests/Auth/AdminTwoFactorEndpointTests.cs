using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Email;
using APCS.Application.Features.Auth.Common;
using APCS.Common.Constants;
using APCS.Common.Helpers;
using APCS.IntegrationTests.TestSupport;
using APCS.IntegrationTests.TestSupport.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace APCS.IntegrationTests.Auth;

/// <summary>
/// Covers the administrator email OTP second factor over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// An administrator sign-in stops after the password check and issues a time-bound six-digit
/// code through the same email fake that carries the verification and reset links, so the OTP
/// is read back out of the recorded body exactly as the SMTP sink test does with a regex. The
/// challenge itself lives in Redis under a hashed temp-token key with a five-minute code lifetime
/// inside a thirty-minute resend window.
/// </para>
/// <para>
/// Expiry is forced by rewriting the cached challenge with a past deadline rather than by
/// waiting out the five minutes, because the deadline is enforced by a comparison in the use
/// case. The SMTP sink file is not involved here: the integration host never reaches a real
/// mail server, and the fake inbox is the only carrier of the code.
/// </para>
/// </remarks>
[TestClass]
public sealed class AdminTwoFactorEndpointTests
{
    private const string Password = "Passw0rdTest1!";
    private const string LoginRoute = "/api/auth/login";
    private const string VerifyRoute = "/api/auth/admin/verify-2fa";
    private const string ResendRoute = "/api/auth/admin/resend-otp";
    private const string MeRoute = "/api/auth/me";

    private static readonly Regex OtpPattern = new(@"(\d{6})", RegexOptions.Compiled);

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
    public async Task AdminLogin_WhenTheAccountIsAnAdministrator_RequiresTwoFactor()
    {
        _host.EnsureAvailable();
        var email = await RegisterVerifyAndPromoteAsync();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(LoginRoute, new { email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("requiresTwoFactor").GetBoolean().Should().BeTrue();
        body.GetProperty("tempToken").GetString().Should().NotBeNullOrWhiteSpace();
        response.Headers.Contains("Set-Cookie").Should().BeFalse("an administrator login issues no session before the OTP");
    }

    [TestMethod]
    public async Task AdminLogin_WhenTheAccountIsAnAdministrator_EmailsASixDigitCode()
    {
        _host.EnsureAvailable();
        var email = await RegisterVerifyAndPromoteAsync();
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(LoginRoute, new { email, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var message = inbox.Messages
            .Skip(before)
            .Single(recorded => string.Equals(recorded.Recipient, email, StringComparison.OrdinalIgnoreCase));
        OtpPattern.IsMatch(message.Body).Should().BeTrue("the administrator email must carry a six-digit code");
    }

    [TestMethod]
    public async Task SellerLogin_WhenTheAccountIsNotAnAdministrator_DoesNotRequireTwoFactor()
    {
        _host.EnsureAvailable();
        var account = await TestAccount.SignInAsync(_host, TestAccount.Unique("seller") + "@apcs.test");

        using var response = await account.Client.PostAsJsonAsync(
            LoginRoute,
            new { email = account.Email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("requiresTwoFactor").GetBoolean().Should().BeFalse();
    }

    [TestMethod]
    public async Task VerifyTwoFactor_WhenTheCodeIsCorrect_AnswersTheAdministrator()
    {
        _host.EnsureAvailable();
        var email = await RegisterVerifyAndPromoteAsync();
        var (tempToken, otp) = await StartChallengeAsync(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(VerifyRoute, new { tempToken, otpCode = otp });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("requiresTwoFactor").GetBoolean().Should().BeFalse();
        body.GetProperty("user").GetProperty("email").GetString().Should().Be(email);
    }

    [TestMethod]
    public async Task VerifyTwoFactor_WhenTheCodeIsCorrect_IssuesARefreshCookie()
    {
        _host.EnsureAvailable();
        var email = await RegisterVerifyAndPromoteAsync();
        var (tempToken, otp) = await StartChallengeAsync(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(VerifyRoute, new { tempToken, otpCode = otp });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ReadSetCookies(response).Should().ContainKey(
            AuthConstants.RefreshTokenCookieName,
            "a successful second factor must establish the refresh session");
    }

    [TestMethod]
    public async Task VerifyTwoFactor_WhenTheCodeIsWrong_IsRefused()
    {
        _host.EnsureAvailable();
        var email = await RegisterVerifyAndPromoteAsync();
        var (tempToken, _) = await StartChallengeAsync(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(VerifyRoute, new { tempToken, otpCode = "000000" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.AdminTwoFactorInvalid);
    }

    [TestMethod]
    public async Task VerifyTwoFactor_WhenTheTempTokenIsUnknown_IsRefused()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(
            VerifyRoute,
            new { tempToken = "unknown-temp-token", otpCode = "123456" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.AdminTwoFactorInvalid);
    }

    [TestMethod]
    public async Task VerifyTwoFactor_WhenTheCodeIsMalformed_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var email = await RegisterVerifyAndPromoteAsync();
        var (tempToken, _) = await StartChallengeAsync(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(VerifyRoute, new { tempToken, otpCode = "not-six-digits" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task VerifyTwoFactor_WhenTheCodeIsExpired_IsRefused()
    {
        _host.EnsureAvailable();
        var email = await RegisterVerifyAndPromoteAsync();
        var (tempToken, otp) = await StartChallengeAsync(email);
        await ExpireChallengeAsync(tempToken);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(VerifyRoute, new { tempToken, otpCode = otp });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.AdminTwoFactorExpired);
    }

    [TestMethod]
    public async Task ResendOtp_WhenTheChallengeIsPending_AnswersWithFreshExpiry()
    {
        _host.EnsureAvailable();
        var email = await RegisterVerifyAndPromoteAsync();
        var (tempToken, _) = await StartChallengeAsync(email);

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ResendRoute, new { tempToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tempToken").GetString().Should().Be(tempToken);
        body.GetProperty("expiresAtUtc").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [TestMethod]
    public async Task ResendOtp_WhenTheChallengeIsPending_EmailsAReplacementCode()
    {
        _host.EnsureAvailable();
        var email = await RegisterVerifyAndPromoteAsync();
        var (tempToken, firstOtp) = await StartChallengeAsync(email);
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.PostAsJsonAsync(ResendRoute, new { tempToken });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var replacement = ReadLatestOtp(email, before);
        replacement.Should().NotBeNullOrWhiteSpace();
        replacement.Should().NotBe(firstOtp, "a resend must rotate the code");

        using var stale = await client.PostAsJsonAsync(VerifyRoute, new { tempToken, otpCode = firstOtp });
        await stale.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.AdminTwoFactorInvalid);

        using var freshClient = TestAccount.CreateAnonymousClient(_host);
        using var fresh = await freshClient.PostAsJsonAsync(VerifyRoute, new { tempToken, otpCode = replacement });
        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task ResendOtp_WhenTheTempTokenIsUnknown_IsRefused()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(ResendRoute, new { tempToken = "unknown-temp-token" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.AdminTwoFactorInvalid);
    }

    private static FakeEmailService ReadInbox()
    {
        var inbox = _host.Factory.Services.GetService<IEmailService>();

        inbox.Should().BeOfType<FakeEmailService>("the host must send through the recording fake");
        return (FakeEmailService)inbox!;
    }

    private static async Task<string> RegisterVerifyAndPromoteAsync()
    {
        var email = TestAccount.Unique("admin2fa") + "@apcs.test";
        var account = await TestAccount.SignInAsync(_host, email, Password);

        var seeder = AuthTestSeeder.Create(_host.Factory.Services);
        var adminRoleId = await seeder.SeedRoleAsync(AuthConstants.AdminRole, AuthConstants.AdminRole);
        await seeder.GrantRoleAsync(account.UserId, adminRoleId);

        return email;
    }

    private static async Task<(string TempToken, string Otp)> StartChallengeAsync(string email)
    {
        var inbox = ReadInbox();
        var before = inbox.Messages.Count;

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var login = await client.PostAsJsonAsync(LoginRoute, new { email, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var tempToken = body.GetProperty("tempToken").GetString();
        tempToken.Should().NotBeNullOrWhiteSpace();

        var otp = ReadLatestOtp(email, before);
        otp.Should().MatchRegex(@"^\d{6}$", "the emailed code must be six digits");

        return (tempToken!, otp);
    }

    private static string ReadLatestOtp(string email, int skip)
    {
        var inbox = ReadInbox();
        var message = inbox.Messages
            .Skip(skip)
            .LastOrDefault(recorded => string.Equals(recorded.Recipient, email, StringComparison.OrdinalIgnoreCase));

        message.Should().NotBeNull("the host must email an administrator code for every admin login");

        var match = OtpPattern.Match(message!.Body);
        match.Success.Should().BeTrue("the recorded body must carry the six-digit code");

        return match.Groups[1].Value;
    }

    private static async Task ExpireChallengeAsync(string tempToken)
    {
        var cache = _host.Factory.Services.GetRequiredService<ICacheService>();
        var key = $"{AuthConstants.AdminTwoFactorCacheKeyPrefix}{HashHelper.ComputeSha256Hash(tempToken)}";
        var challenge = await cache.GetAsync<AdminTwoFactorChallenge>(key);

        challenge.Should().NotBeNull("a pending challenge must exist to expire");

        var expired = challenge! with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        await cache.SetAsync(key, expired, TimeSpan.FromMinutes(AuthConstants.AdminTwoFactorChallengeRetentionMinutes));
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
}
