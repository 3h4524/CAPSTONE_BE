using System.Net;
using System.Net.Http.Json;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;

namespace APCS.IntegrationTests.Auth;

/// <summary>
/// Covers the authenticated change-password endpoint over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Change-password is the only password write that runs with a session instead of a single-use
/// link, so its guarantees differ from reset-password: the current password must be verified
/// against the stored hash, a wrong value is a bad input rather than an authentication failure,
/// and the session itself decides who may call. Every case here therefore drives the session
/// cookie a browser client holds, not a minted token.
/// </para>
/// <para>
/// A wrong current password answers 400 with <c>auth.password_incorrect</c> rather than 401 on
/// purpose, so the client retry logic that reacts to 401 never mistakes a typo for an expired
/// session. The test pins that contract explicitly.
/// </para>
/// </remarks>
[TestClass]
public sealed class ChangePasswordEndpointTests
{
    private const string Password = "Passw0rdTest1!";
    private const string NewPassword = "ChangedPass1!";
    private const string ChangeRoute = "/api/auth/change-password";
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
    public async Task ChangePassword_WhenTheCurrentPasswordIsCorrect_AnswersNoContent()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            ChangeRoute,
            new { currentPassword = Password, newPassword = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task ChangePassword_WhenTheCurrentPasswordIsCorrect_AllowsSignInWithNewPassword()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var change = await account.Client.PostAsJsonAsync(
            ChangeRoute,
            new { currentPassword = Password, newPassword = NewPassword });

        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var anonymous = TestAccount.CreateAnonymousClient(_host);
        using var loginWithNew = await anonymous.PostAsJsonAsync(
            LoginRoute,
            new { email = account.Email, password = NewPassword });

        loginWithNew.StatusCode.Should().Be(HttpStatusCode.OK);

        using var secondAnonymous = TestAccount.CreateAnonymousClient(_host);
        using var loginWithOld = await secondAnonymous.PostAsJsonAsync(
            LoginRoute,
            new { email = account.Email, password = Password });

        await loginWithOld.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    [TestMethod]
    public async Task ChangePassword_WhenTheCurrentPasswordIsWrong_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            ChangeRoute,
            new { currentPassword = "WrongPass1!", newPassword = NewPassword });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.PasswordIncorrect);
    }

    [TestMethod]
    public async Task ChangePassword_WhenTheCurrentPasswordIsWrong_KeepsTheOldPassword()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var change = await account.Client.PostAsJsonAsync(
            ChangeRoute,
            new { currentPassword = "WrongPass1!", newPassword = NewPassword });

        change.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var anonymous = TestAccount.CreateAnonymousClient(_host);
        using var login = await anonymous.PostAsJsonAsync(
            LoginRoute,
            new { email = account.Email, password = Password });

        login.StatusCode.Should().Be(HttpStatusCode.OK, "a rejected change must not rotate the password");
    }

    [TestMethod]
    public async Task ChangePassword_WhenTheNewPasswordIsWeak_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            ChangeRoute,
            new { currentPassword = Password, newPassword = "weak" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task ChangePassword_WhenTheCallerIsNotSignedIn_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(
            ChangeRoute,
            new { currentPassword = Password, newPassword = NewPassword });

        await response.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task ChangePassword_WhenTheCurrentPasswordIsMissing_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            ChangeRoute,
            new { currentPassword = string.Empty, newPassword = NewPassword });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    private static Task<AuthenticatedAccount> SignInAsync() =>
        TestAccount.SignInAsync(_host, TestAccount.Unique("changepwd") + "@apcs.test");
}
