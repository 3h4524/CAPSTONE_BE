using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Email;
using APCS.IntegrationTests.TestSupport.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace APCS.IntegrationTests.TestSupport;

/// <summary>
/// Truncates every table of the database named by <c>APCS_TEST_RESET_DATABASE</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PostgresFixture"/> deliberately keeps Respawn switched off for an externally supplied
/// endpoint, because truncating a database it does not own is not its decision to make. That is the
/// right default, but it also means a suite pointed at a database created purely for these tests
/// starts every test with whatever the previous one left behind, which makes list and uniqueness
/// assertions meaningless.
/// </para>
/// <para>
/// This type closes that gap without weakening the shared fixture: it truncates only when the
/// caller explicitly names the database in <c>APCS_TEST_RESET_DATABASE</c> and the connection string
/// actually points at a database with that name. Point the variable at somebody else's database and
/// the call is a no-op, so a developer cannot lose data by exporting the wrong value.
/// </para>
/// </remarks>
public static class DedicatedDatabaseReset
{
    /// <summary>The environment variable that opts a database into truncation.</summary>
    public const string ResetDatabaseVariable = "APCS_TEST_RESET_DATABASE";

    /// <summary>
    /// Reports whether the connection string points at the database the caller opted into.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string the host runs against.</param>
    /// <returns><see langword="true"/> when truncation is permitted.</returns>
    public static bool IsEnabled(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var approved = Environment.GetEnvironmentVariable(ResetDatabaseVariable);

        return !string.IsNullOrWhiteSpace(approved)
            && string.Equals(ReadDatabase(connectionString), approved, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Empties every table in the opted-in database so the next test starts from a known state.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string the host runs against.</param>
    /// <returns>A task that completes once the truncation has run, or immediately when not permitted.</returns>
    public static async Task TruncateAsync(string? connectionString, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled(connectionString))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            DO $$
            DECLARE
                statement text;
            BEGIN
                FOR statement IN
                    SELECT 'TRUNCATE TABLE public.' || quote_ident(tablename) || ' RESTART IDENTITY CASCADE'
                    FROM pg_tables
                    WHERE schemaname = 'public'
                LOOP
                    EXECUTE statement;
                END LOOP;
            END $$;
            """,
            connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Empties the database the host is currently configured against, when that database was opted
    /// into truncation.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the truncation has run, or immediately when not permitted.</returns>
    public static Task TruncateConfiguredAsync(CancellationToken cancellationToken = default) =>
        TruncateAsync(TestEnvironment.Get("ConnectionStrings:DefaultConnection"), cancellationToken);

    private static string? ReadDatabase(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).Database;
}

/// <summary>
/// A signed-in account plus the client that carries its session.
/// </summary>
/// <param name="Email">The registered address.</param>
/// <param name="Password">The password the account was registered with.</param>
/// <param name="UserId">The account identifier returned by registration.</param>
/// <param name="Client">A client holding the account's session cookie.</param>
/// <param name="EmailService">The fake inbox that captured the account's verification link.</param>
public sealed record AuthenticatedAccount(
    string Email,
    string Password,
    Guid UserId,
    HttpClient Client,
    FakeEmailService EmailService);

/// <summary>
/// Drives the real HTTP endpoints a browser client would use to obtain and keep a session.
/// </summary>
/// <remarks>
/// <para>
/// A test cannot mint its own credential: the JWT signing key belongs to the host, and the whole
/// point of these tests is that the authentication stack issues the session. Every account is
/// therefore registered, verified from the link the host emailed, and logged in through the public
/// endpoints, which also proves those three steps fit together.
/// </para>
/// <para>
/// The session client is created against an <c>https</c> base address on purpose. The API issues its
/// cookies with <c>Secure</c>, and a cookie jar refuses to replay a secure cookie over a plain
/// scheme, so the default <c>http://localhost</c> base address silently drops the session on the very
/// next request. Production sits behind TLS, so <c>https</c> is the scheme the contract is written
/// for, and using it keeps these tests on the same code path the browser client takes.
/// </para>
/// </remarks>
public static class TestAccount
{
    /// <summary>The token query key the verification link carries.</summary>
    private const string TokenQueryKey = "token=";

    /// <summary>The secure origin the session cookies are issued for.</summary>
    public static readonly Uri SecureOrigin = new("https://localhost");

    /// <summary>
    /// Registers, verifies, and signs in an account, returning a client holding its session.
    /// </summary>
    /// <param name="host">The running integration host.</param>
    /// <param name="email">The address to register. Must be unique within the database.</param>
    /// <param name="password">The password to register with.</param>
    /// <param name="fullName">The display name to register with.</param>
    /// <returns>The signed-in account and its client.</returns>
    public static async Task<AuthenticatedAccount> SignInAsync(
        IntegrationTestHost host,
        string email,
        string password = "Passw0rdTest1!",
        string fullName = "Test Seller")
    {
        ArgumentNullException.ThrowIfNull(host);

        var inbox = ReadInbox(host);
        var messageCountBeforeRegistration = inbox.Messages.Count;

        using var anonymous = CreateAnonymousClient(host);
        using var registration = await anonymous.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password, fullName });

        registration.StatusCode.Should().Be(HttpStatusCode.OK, "the registration request must be accepted");

        var body = await registration.Content.ReadFromJsonAsync<JsonElement>();
        var userId = body.GetProperty("userId").GetGuid();
        var verificationToken = ReadLatestVerificationToken(inbox, messageCountBeforeRegistration, email);

        using var verification = await anonymous.PostAsJsonAsync("/api/auth/verify-email", new { token = verificationToken });
        verification.StatusCode.Should().Be(HttpStatusCode.NoContent, "the verification link must be accepted");

        var client = CreateAnonymousClient(host);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK, "a verified account must be able to sign in");

        return new AuthenticatedAccount(email, password, userId, client, inbox);
    }

    /// <summary>
    /// Creates a client that carries session cookies exactly as a browser client does.
    /// </summary>
    /// <param name="host">The running integration host.</param>
    /// <returns>A cookie-carrying client bound to the secure origin.</returns>
    public static HttpClient CreateAnonymousClient(IntegrationTestHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return host.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = SecureOrigin,
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    /// <summary>
    /// Builds a value unique to this run, so a suite that does not reset its database still passes.
    /// </summary>
    /// <param name="prefix">The recognizable part of the value.</param>
    /// <returns>The prefix followed by a run-unique suffix.</returns>
    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>
    /// Reads the verification token the host emailed, the same way a user reads it out of the link.
    /// </summary>
    /// <param name="inbox">The fake inbox standing in for the mail server.</param>
    /// <param name="skip">How many already recorded messages precede the one to read.</param>
    /// <param name="recipient">The address whose message to read.</param>
    /// <returns>The raw verification token.</returns>
    public static string ReadLatestVerificationToken(FakeEmailService inbox, int skip, string recipient)
    {
        ArgumentNullException.ThrowIfNull(inbox);

        var message = inbox.Messages
            .Skip(skip)
            .LastOrDefault(candidate => string.Equals(candidate.Recipient, recipient, StringComparison.OrdinalIgnoreCase));

        message.Should().NotBeNull("the host must email a verification link for every registration");

        var separator = message!.Body.LastIndexOf(TokenQueryKey, StringComparison.Ordinal);
        separator.Should().BeGreaterThanOrEqualTo(0, "the recorded body must carry the verification link");

        return Uri.UnescapeDataString(message.Body[(separator + TokenQueryKey.Length)..].Trim());
    }

    /// <summary>
    /// Builds a multipart body for the style preset endpoints.
    /// </summary>
    /// <param name="fields">The text fields, or <see langword="null"/> to omit one.</param>
    /// <param name="preview">The preview file, or <see langword="null"/> to omit it.</param>
    /// <returns>A multipart body ready to post.</returns>
    public static MultipartFormDataContent BuildStylePresetForm(
        IReadOnlyDictionary<string, string>? fields = null,
        PreviewFile? preview = null)
    {
        var form = new MultipartFormDataContent();

        if (fields is not null)
        {
            foreach (var (key, value) in fields)
            {
                form.Add(new StringContent(value, Encoding.UTF8), key);
            }
        }

        if (preview is not null)
        {
            var file = new ByteArrayContent(preview.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(preview.ContentType);
            form.Add(file, "Preview", preview.FileName);
        }

        return form;
    }

    private static FakeEmailService ReadInbox(IntegrationTestHost host)
    {
        var inbox = host.Factory.Services.GetRequiredService<IEmailService>();

        inbox.Should().BeOfType<FakeEmailService>("the host must send through the recording fake");
        return (FakeEmailService)inbox;
    }
}

/// <summary>
/// A file to upload through a multipart request.
/// </summary>
/// <param name="FileName">The client-supplied file name.</param>
/// <param name="ContentType">The declared content type, which the validators read.</param>
/// <param name="Content">The bytes to send.</param>
public sealed record PreviewFile(string FileName, string ContentType, byte[] Content)
{
    /// <summary>A minimal, valid, one-pixel PNG.</summary>
    public static PreviewFile OnePixelPng(string fileName = "preview.png") =>
        new(
            fileName,
            "image/png",
            [
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
                0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
                0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
                0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
                0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
                0x42, 0x60, 0x82
            ]);
}

/// <summary>
/// Reads the error contract the application returns for an expected failure.
/// </summary>
/// <remarks>
/// Two shapes reach a client. A rejected use case returns a <see cref="ProblemDetails"/> carrying the
/// canonical <c>code</c> extension, while model binding rejects a malformed request before any use
/// case runs and returns a <c>ValidationProblemDetails</c> with only the field errors. Both are
/// 400, so a test that only cares that the input was rejected must not demand the code extension.
/// </remarks>
public static class ProblemResponses
{
    /// <summary>
    /// Asserts the status code of a rejected request without inspecting the body.
    /// </summary>
    /// <param name="response">The rejected response.</param>
    /// <param name="expectedStatus">The status the contract promises.</param>
    /// <returns>A task that completes once the status has been asserted.</returns>
    public static async Task ShouldFailAsync(this HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.StatusCode.Should().Be(expectedStatus);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Asserts the status code and the canonical error code of a rejected use case.
    /// </summary>
    /// <param name="response">The rejected response.</param>
    /// <param name="expectedStatus">The status the contract promises.</param>
    /// <param name="expectedCode">The canonical error code the use case must return.</param>
    /// <returns>The canonical error code the response carried.</returns>
    public static async Task<string> ShouldFailWithCodeAsync(
        this HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCode);

        var code = await ReadCodeAsync(response, expectedStatus);
        code.Should().Be(expectedCode);

        return code;
    }

    /// <summary>
    /// Asserts that model binding rejected the request and named the offending field.
    /// </summary>
    /// <remarks>
    /// A form that omits or blanks a non-nullable text field is rejected before any use case runs,
    /// so the answer carries the per-field errors of a <see cref="ValidationProblemDetails"/> and no
    /// canonical <c>code</c>. Asserting on the field name keeps such a test from passing on any
    /// unrelated 400 the pipeline could produce.
    /// </remarks>
    /// <param name="response">The rejected response.</param>
    /// <param name="expectedStatus">The status the contract promises.</param>
    /// <param name="expectedField">The field the rejection must name.</param>
    /// <returns>A task that completes once the field errors have been asserted.</returns>
    public static async Task ShouldFailValidationAsync(
        this HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedField)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedField);

        response.StatusCode.Should().Be(expectedStatus);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem.Should().NotBeNull("a rejected request must answer with field errors");
        problem!.Errors.Should().ContainKey(expectedField, "the rejection must name the offending field");
    }

    /// <summary>
    /// Reads the canonical error code out of a rejected use case response.
    /// </summary>
    /// <param name="response">The rejected response.</param>
    /// <param name="expectedStatus">The status the contract promises.</param>
    /// <returns>The canonical error code the response carried.</returns>
    public static async Task<string> ReadCodeAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.StatusCode.Should().Be(expectedStatus);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull("an expected failure must answer with problem details");
        problem!.Status.Should().Be((int)expectedStatus);
        problem.Extensions.Should().ContainKey("code", "a rejected use case must answer with a canonical error code");

        return ((JsonElement)problem.Extensions["code"]!).GetString()!;
    }
}