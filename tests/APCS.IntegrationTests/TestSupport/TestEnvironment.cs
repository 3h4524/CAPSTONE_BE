namespace APCS.IntegrationTests.TestSupport;

/// <summary>
/// Seeds the test process environment before the API entry point is executed.
/// </summary>
/// <remarks>
/// <para>
/// <c>API/Program.cs</c> calls <c>DotEnvLoader.Load()</c> and then reads required options while the
/// host is still being built. <c>DotEnvLoader</c> skips any key that already exists in the process
/// environment, and an empty string counts as existing. Pre-seeding every third-party key here
/// therefore keeps the repository <c>.env</c> secrets out of the test process entirely, instead of
/// merely overriding them after the host is built.
/// </para>
/// <para>
/// Required keys are seeded with deterministic test values so the host boots identically with or
/// without a developer <c>.env</c> file. The PostgreSQL and Redis values are placeholders that
/// <see cref="PostgresFixture"/> replaces with the real container endpoints before the host boots.
/// </para>
/// </remarks>
public static class TestEnvironment
{
    /// <summary>The ASP.NET Core environment the integration host runs under.</summary>
    public const string EnvironmentName = "Testing";

    /// <summary>Environment variable that supplies a PostgreSQL endpoint when Docker is unavailable.</summary>
    public const string PostgresConnectionVariable = "APCS_TEST_CONNECTION_STRING";

    /// <summary>Environment variable that supplies a Redis endpoint when Docker is unavailable.</summary>
    public const string RedisConnectionVariable = "APCS_TEST_REDIS_CONNECTION_STRING";

    /// <summary>
    /// A deterministic signing key. It is not a secret: it is compiled into the test assembly and is
    /// only ever used to sign tokens for a host that listens on an in-memory test server.
    /// </summary>
    public const string SigningKey = "apcs.integration.test.signing.key.0123456789abcdef";

    /// <summary>The placeholder PostgreSQL connection string used until a fixture supplies a real one.</summary>
    public const string PlaceholderPostgresConnectionString =
        "Host=localhost;Port=5432;Database=apcs;Username=apcs;Password=apcs";

    /// <summary>The placeholder Redis connection string used until a fixture supplies a real one.</summary>
    public const string PlaceholderRedisConnectionString = "localhost:6379,abortConnect=false";

    private static readonly IReadOnlyDictionary<string, string> RequiredKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Jwt:Issuer"] = "APCS.IntegrationTests",
            ["Jwt:Audience"] = "APCS.Web",
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:AccessTokenMinutes"] = "15",
            ["Jwt:RefreshTokenDays"] = "14",
            ["App:BaseUrl"] = "http://localhost:3000",
            ["App:VerifyEmailPath"] = "/verify-email",
            ["App:ResetPasswordPath"] = "/reset-password",
            ["Redis:InstanceName"] = "APCS:test:",
            ["Redis:DefaultExpirationMinutes"] = "10",
            ["Cors:AllowedOrigins"] = "http://localhost:3000"
        };

    private static readonly string[] ConnectionStringKeys =
    [
        "ConnectionStrings:DefaultConnection",
        "Redis:ConnectionString"
    ];

    /// <summary>
    /// Third-party credentials that must never be read from the developer <c>.env</c>. An empty
    /// value is written for every string key, so the corresponding adapter finds itself
    /// unconfigured; numeric and boolean keys receive a neutral value because the configuration
    /// binder cannot read an empty string into them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ThirdPartyKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Smtp:Host"] = string.Empty,
            ["Smtp:Port"] = "0",
            ["Smtp:UseStartTls"] = "false",
            ["Smtp:Username"] = string.Empty,
            ["Smtp:Password"] = string.Empty,
            ["Smtp:FromAddress"] = string.Empty,
            ["Smtp:FromName"] = string.Empty,
            ["Authentication:Google:ClientId"] = string.Empty,
            ["Cloudinary:CloudName"] = string.Empty,
            ["Cloudinary:ApiKey"] = string.Empty,
            ["Cloudinary:ApiSecret"] = string.Empty,
            ["Cloudinary:Folder"] = "apcs-test",
            ["Cloudinary:SignedUrlTtlMinutes"] = "10",
            ["PayOS:ClientId"] = string.Empty,
            ["PayOS:ApiKey"] = string.Empty,
            ["PayOS:ChecksumKey"] = string.Empty,
            ["PayOS:UsdToVndRate"] = "25000",
            ["PayOS:TestAmountVnd"] = "0"
        };

    private static readonly object Gate = new();
    private static bool _applied;

    /// <summary>
    /// Gets a value indicating whether <see cref="Apply"/> has already run.
    /// </summary>
    public static bool IsApplied
    {
        get
        {
            lock (Gate)
            {
                return _applied;
            }
        }
    }

    /// <summary>
    /// Writes the deterministic test configuration and blocks every third-party credential.
    /// </summary>
    /// <remarks>
    /// Explicit values already present in the environment win, so CI can inject a real endpoint
    /// without the harness overwriting it.
    /// </remarks>
    public static void Apply()
    {
        lock (Gate)
        {
            if (_applied)
            {
                return;
            }

            foreach (var (key, value) in RequiredKeys)
            {
                SetIfAbsent(key, value);
            }

            foreach (var key in ConnectionStringKeys)
            {
                SetIfAbsent(key, key.StartsWith("Redis", StringComparison.Ordinal)
                    ? PlaceholderRedisConnectionString
                    : PlaceholderPostgresConnectionString);
            }

            foreach (var (key, value) in ThirdPartyKeys)
            {
                Set(key, value);
            }

            _applied = true;
        }
    }

    /// <summary>
    /// Overwrites an environment variable, whichever notation the host configuration reads.
    /// </summary>
    /// <param name="configurationKey">The colon-separated configuration key.</param>
    /// <param name="value">The value to publish.</param>
    public static void Set(string configurationKey, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);

        Environment.SetEnvironmentVariable(configurationKey, value);
        Environment.SetEnvironmentVariable(
            configurationKey.Replace(":", "__", StringComparison.Ordinal),
            value);
    }

    /// <summary>
    /// Reads an environment variable written by <see cref="Set"/>.
    /// </summary>
    /// <param name="configurationKey">The colon-separated configuration key.</param>
    /// <returns>The current value, or <see langword="null"/> when it is not set.</returns>
    public static string? Get(string configurationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);

        return Environment.GetEnvironmentVariable(configurationKey)
            ?? Environment.GetEnvironmentVariable(
                configurationKey.Replace(":", "__", StringComparison.Ordinal));
    }

    private static void SetIfAbsent(string configurationKey, string value)
    {
        if (Get(configurationKey) is null)
        {
            Set(configurationKey, value);
        }
    }
}
