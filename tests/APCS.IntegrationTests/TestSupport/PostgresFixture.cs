using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
    /// The image the production database runs. The entity model creates columns whose types need
    /// the pgvector extension, so the image has to ship it.
    /// </summary>
    private const string PostgresImage = "pgvector/pgvector:pg18";

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
    /// the schema built from the entity model keeps describing the shape the application maps.
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

    private static async Task<PostgresFixture> StartAsync()
    {
        TestEnvironment.Apply();

        if (!DockerIsReachable())
        {
            return await FromEnvironment(dockerFailure: null).ConfigureAwait(false);
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
            await ApplyEntityModelAsync(postgres.GetConnectionString()).ConfigureAwait(false);
            await redis.StartAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await postgres.DisposeAsync().ConfigureAwait(false);
            await redis.DisposeAsync().ConfigureAwait(false);
            return await FromEnvironment(exception).ConfigureAwait(false);
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
    /// Creates the database schema from the entity framework model.
    /// </summary>
    /// <remarks>
    /// The repository is database-first and ships no migrations for tests, so the model itself is
    /// the single source of the schema: every extension the mappings need is created first and
    /// <c>EnsureCreated</c> materializes every table. A future entity change therefore needs no
    /// schema artifact to be maintained by hand.
    /// </remarks>
    /// <param name="connectionString">The container or provided database connection string.</param>
    /// <returns>A task that completes once the schema exists.</returns>
    private static async Task ApplyEntityModelAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\"", connection)
            .ExecuteNonQueryAsync().ConfigureAwait(false);
        await new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS \"vector\"", connection)
            .ExecuteNonQueryAsync().ConfigureAwait(false);

        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    private static async Task<PostgresFixture> FromEnvironment(Exception? dockerFailure)
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

        await ApplyEntityModelAsync(connectionString).ConfigureAwait(false);

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
