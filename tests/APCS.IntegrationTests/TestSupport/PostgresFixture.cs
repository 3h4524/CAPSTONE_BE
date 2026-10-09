using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace APCS.IntegrationTests.TestSupport;

/// <summary>
/// Owns the PostgreSQL and Redis endpoints the integration host runs against.
/// </summary>
/// <remarks>
/// <para>
/// Testcontainers are preferred: disposable containers plus a Respawn reset cost roughly 50-200 ms
/// per test, against several seconds for a container start per test. Both containers are therefore
/// started once per test class and shared by every test in it.
/// </para>
/// <para>
/// When Docker is unavailable the fixture falls back to the endpoints named by
/// <c>APCS_TEST_CONNECTION_STRING</c> and <c>APCS_TEST_REDIS_CONNECTION_STRING</c>, matching the
/// convention already used by <c>APCS.Infrastructure.IntegrationTests</c>. Respawn stays disabled on
/// that path: truncating a developer or shared database is not this fixture's decision to make.
/// When neither Docker nor those variables are available, <see cref="EnsureAvailable"/> reports the
/// suite as inconclusive instead of failing.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncDisposable
{
    /// <summary>
    /// The image the production database runs. The committed bootstrap dump is taken from
    /// PostgreSQL 18, so the container matches it exactly instead of replaying newer syntax such as
    /// <c>SET transaction_timeout</c> onto an older server.
    /// </summary>
    private const string PostgresImage = "postgres:18-alpine";

    private const string RedisImage = "redis:7-alpine";

    private const string DatabaseName = "apcs";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgresFixture? _shared;
    private static int _referenceCount;

    private readonly PostgreSqlContainer? _postgres;
    private readonly RedisContainer? _redis;
    private Respawner? _respawner;
    private NpgsqlConnection? _respawnConnection;

    private PostgresFixture(
        PostgreSqlContainer? postgres,
        RedisContainer? redis,
        string? postgresConnectionString,
        string? redisConnectionString,
        bool ownsContainers,
        string? unavailableReason)
    {
        _postgres = postgres;
        _redis = redis;
        PostgresConnectionString = postgresConnectionString;
        RedisConnectionString = redisConnectionString;
        OwnsContainers = ownsContainers;
        UnavailableReason = unavailableReason;
    }

    /// <summary>Gets the Npgsql connection string the host is configured with.</summary>
    public string? PostgresConnectionString { get; }

    /// <summary>Gets the StackExchange.Redis connection string the host is configured with.</summary>
    public string? RedisConnectionString { get; }

    /// <summary>
    /// Gets a value indicating whether the endpoints are disposable Testcontainers. When
    /// <see langword="false"/> the endpoints are externally supplied and must never be truncated.
    /// </summary>
    public bool OwnsContainers { get; }

    /// <summary>Gets a value indicating whether the host can be started.</summary>
    public bool IsAvailable => UnavailableReason is null;

    /// <summary>
    /// Gets why the fixture cannot provide an endpoint, or <see langword="null"/> when it can.
    /// </summary>
    public string? UnavailableReason { get; }

    /// <summary>
    /// Acquires the process-wide fixture, starting the containers on the first call.
    /// </summary>
    /// <returns>The shared fixture. Every acquired lease must be released.</returns>
    public static async Task<PostgresFixture> AcquireAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _shared ??= await StartAsync().ConfigureAwait(false);
            _referenceCount++;
            return _shared;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Releases a lease taken by <see cref="AcquireAsync"/>, stopping the containers once the last
    /// lease is returned.
    /// </summary>
    public static async Task ReleaseAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_referenceCount == 0)
            {
                return;
            }

            if (--_referenceCount > 0 || _shared is null)
            {
                return;
            }

            var fixture = _shared;
            _shared = null;
            await fixture.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Truncates every table so the next test starts from a known state.
    /// </summary>
    /// <remarks>
    /// A no-op unless the fixture owns disposable containers. Respawn keeps the schema in place, so
    /// the committed bootstrap dump keeps describing the shape the application maps.
    /// </remarks>
    public async Task ResetAsync()
    {
        if (_respawner is null || _respawnConnection is null)
        {
            return;
        }

        await _respawner.ResetAsync(_respawnConnection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_respawnConnection is not null)
        {
            await _respawnConnection.DisposeAsync().ConfigureAwait(false);
            _respawnConnection = null;
        }

        if (_redis is not null)
        {
            await _redis.DisposeAsync().ConfigureAwait(false);
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves the path of the bootstrap dump copied next to the test assembly.
    /// </summary>
    /// <returns>The absolute path of the dump.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the dump is missing from the output.</exception>
    public static string ResolveBootstrapScriptPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "scripts", "sql", "000-bootstrap-schema.sql");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The committed bootstrap schema dump is missing from the test output. Rebuild so the Content item in APCS.IntegrationTests.csproj copies it.",
                path);
        }

        return path;
    }

    private static async Task<PostgresFixture> StartAsync()
    {
        TestEnvironment.Apply();

        if (!DockerIsReachable())
        {
            return FromEnvironment(dockerFailure: null);
        }

        var postgres = new PostgreSqlBuilder(PostgresImage)
            .WithDatabase(DatabaseName)
            .WithUsername(DatabaseName)
            .WithPassword(DatabaseName)
            .Build();

        var redis = new RedisBuilder(RedisImage).Build();

        try
        {
            await postgres.StartAsync().ConfigureAwait(false);
            await ApplyBootstrapSchemaAsync(postgres.GetConnectionString()).ConfigureAwait(false);
            await redis.StartAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await postgres.DisposeAsync().ConfigureAwait(false);
            await redis.DisposeAsync().ConfigureAwait(false);
            return FromEnvironment(exception);
        }

        var fixture = new PostgresFixture(
            postgres,
            redis,
            postgres.GetConnectionString(),
            redis.GetConnectionString(),
            ownsContainers: true,
            unavailableReason: null);

        await fixture.OpenRespawnerAsync().ConfigureAwait(false);
        return fixture;
    }

    /// <summary>
    /// Replays the committed bootstrap dump against a freshly created container database.
    /// </summary>
    /// <remarks>
    /// The repository is database-first and ships no migrations, so the dump taken from the
    /// production server is the only description of the schema. Testcontainers 4 removed
    /// <c>WithInitScript</c>, so the dump is executed over Npgsql once the container's entrypoint
    /// has finished creating the database.
    /// </remarks>
    /// <param name="connectionString">The container connection string.</param>
    /// <returns>A task that completes once every statement in the dump has run.</returns>
    private static async Task ApplyBootstrapSchemaAsync(string connectionString)
    {
        var dump = await File.ReadAllTextAsync(ResolveBootstrapScriptPath()).ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = new NpgsqlCommand(dump, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static PostgresFixture FromEnvironment(Exception? dockerFailure)
    {
        var connectionString = ReadVariable(TestEnvironment.PostgresConnectionVariable);
        var redisConnectionString = ReadVariable(TestEnvironment.RedisConnectionVariable);

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(redisConnectionString))
        {
            return new PostgresFixture(
                postgres: null,
                redis: null,
                postgresConnectionString: null,
                redisConnectionString: null,
                ownsContainers: false,
                unavailableReason: BuildUnavailableReason(dockerFailure));
        }

        // The host resolves both of these while it is being built, before the factory can override
        // configuration, so they have to be published before the first boot.
        TestEnvironment.Set("ConnectionStrings:DefaultConnection", connectionString);
        TestEnvironment.Set("Redis:ConnectionString", redisConnectionString);

        return new PostgresFixture(
            postgres: null,
            redis: null,
            connectionString,
            redisConnectionString,
            ownsContainers: false,
            unavailableReason: null);
    }

    private static string BuildUnavailableReason(Exception? dockerFailure)
    {
        var message =
            "No integration-test PostgreSQL/Redis endpoint is available. Enable Docker Desktop WSL "
            + "integration so Testcontainers can start "
            + PostgresImage
            + " and "
            + RedisImage
            + ", or export both "
            + TestEnvironment.PostgresConnectionVariable
            + " and "
            + TestEnvironment.RedisConnectionVariable
            + " to point at an existing pair.";

        return dockerFailure is null
            ? message
            : message + " Testcontainers reported: " + dockerFailure.Message;
    }

    private async Task OpenRespawnerAsync()
    {
        _respawnConnection = new NpgsqlConnection(PostgresConnectionString);
        await _respawnConnection.OpenAsync().ConfigureAwait(false);

        _respawner = await Respawner.CreateAsync(_respawnConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"]
        }).ConfigureAwait(false);
    }

    private static bool DockerIsReachable()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        return File.Exists("/var/run/docker.sock");
    }

    private static string? ReadVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
