using Microsoft.AspNetCore.Mvc.Testing;

namespace APCS.IntegrationTests.TestSupport;

/// <summary>
/// A started integration host: the provisioned endpoints plus the in-memory API pipeline.
/// </summary>
/// <remarks>
/// A test class takes one lease in <see cref="StartAsync"/> from its
/// <c>[ClassInitialize]</c> and returns it from its <c>[ClassCleanup]</c>. The containers behind the
/// lease are process-wide, so sibling classes reuse them instead of starting their own.
/// </remarks>
public sealed class IntegrationTestHost : IAsyncDisposable
{
    private readonly PostgresFixture _database;
    private readonly ApiFactory? _factory;

    private IntegrationTestHost(PostgresFixture database, ApiFactory? factory)
    {
        _database = database;
        _factory = factory;
    }
    /// <summary>Gets a value indicating whether the API pipeline could be started.</summary>
    public bool IsAvailable => _factory is not null;

    /// <summary>
    /// Creates a client that follows the same cookie-based contract as the browser client.
    /// </summary>
    /// <returns>A client bound to the hosted application.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no endpoint was available.</exception>
    public HttpClient CreateClient() => Factory.CreateAuthenticatedClient();

    /// <summary>
    /// Provisions the endpoints and starts the API pipeline.
    /// </summary>
    /// <returns>A started host, or an unavailable one that reports the suite as inconclusive.</returns>
    public static async Task<IntegrationTestHost> StartAsync()
    {
        var database = await PostgresFixture.AcquireAsync().ConfigureAwait(false);

        if (!database.IsAvailable)
        {
            return new IntegrationTestHost(database, factory: null);
        }

        try
        {
            return new IntegrationTestHost(database, new ApiFactory(database));
        }
        catch
        {
            await PostgresFixture.ReleaseAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Reports the suite as inconclusive when no endpoint could be provisioned.
    /// </summary>
    public void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            Assert.Inconclusive(_database.UnavailableReason ?? string.Empty);
        }
    }

    /// <summary>Gets the hosted application factory.</summary>
    /// <exception cref="InvalidOperationException">Thrown when no endpoint was available.</exception>
    public ApiFactory Factory => _factory ?? throw new InvalidOperationException(
        "The integration host has no usable endpoint. Call EnsureAvailable() before using the factory.");

    /// <summary>
    /// Truncates every table so the current test starts from a known state. A no-op when the
    /// endpoints are externally supplied, because those must not be truncated.
    /// </summary>
    /// <returns>A task that completes once the database is clean.</returns>
    public Task ResetDatabaseAsync() => _database.ResetAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync().ConfigureAwait(false);
        }

        await PostgresFixture.ReleaseAsync().ConfigureAwait(false);
    }
}
