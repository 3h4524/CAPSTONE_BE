using Npgsql;

namespace APCS.Infrastructure.IntegrationTests;

[TestClass]
public sealed class MediaStreamSchemaTests
{
    [TestMethod]
    [DataRow("render")]
    [DataRow("export_zip")]
    public async Task Enqueue_SupportedKind_CreatesVersionedOutboxInSameTransaction(string kind)
    {
        await using var fixture = await SchemaFixture.CreateAsync();
        if (fixture is null) return;
        var id = Guid.NewGuid();

        await fixture.ExecuteAsync("INSERT INTO media_jobs (id, kind, status) VALUES (@id, @kind, 'queued')", id, kind);

        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT queue_version FROM media_jobs WHERE id = @id", id));
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT count(*) FROM media_job_outbox WHERE job_id = @id AND queue_version = 1 AND kind = @kind", id, kind));
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT count(*) FROM media_job_outbox WHERE job_id = @id AND published_at IS NULL AND available_at = next_attempt_at", id));
    }

    [TestMethod]
    public async Task Enqueue_RolledBackSavepoint_RemovesJobAndOutboxTogether()
    {
        await using var fixture = await SchemaFixture.CreateAsync();
        if (fixture is null) return;
        var id = Guid.NewGuid();
        await fixture.ExecuteAsync("SAVEPOINT before_enqueue");
        await fixture.ExecuteAsync("INSERT INTO media_jobs (id, kind, status) VALUES (@id, 'render', 'queued')", id);
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT count(*) FROM media_job_outbox WHERE job_id = @id", id));

        await fixture.ExecuteAsync("ROLLBACK TO SAVEPOINT before_enqueue");

        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT count(*) FROM media_jobs WHERE id = @id", id));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT count(*) FROM media_job_outbox WHERE job_id = @id", id));
    }

    [TestMethod]
    public async Task Requeue_AfterLease_IncrementsVersionAndCreatesOneNewEvent()
    {
        await using var fixture = await SchemaFixture.CreateAsync();
        if (fixture is null) return;
        var id = Guid.NewGuid();
        await fixture.ExecuteAsync("INSERT INTO media_jobs (id, kind, status) VALUES (@id, 'render', 'queued')", id);
        await fixture.ExecuteAsync("UPDATE media_jobs SET status = 'leased', attempt = 1 WHERE id = @id", id);
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT queue_version FROM media_jobs WHERE id = @id", id));

        await fixture.ExecuteAsync("UPDATE media_jobs SET status = 'queued', available_at = available_at + interval '10 seconds' WHERE id = @id", id);
        await fixture.ExecuteAsync("UPDATE media_jobs SET lease_expires_at = now() WHERE id = @id", id);

        Assert.AreEqual(2L, await fixture.ScalarAsync("SELECT queue_version FROM media_jobs WHERE id = @id", id));
        Assert.AreEqual(2L, await fixture.ScalarAsync("SELECT count(*) FROM media_job_outbox WHERE job_id = @id", id));
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT count(*) FROM media_job_outbox WHERE job_id = @id AND queue_version = 2 AND available_at = next_attempt_at", id));
    }

    [TestMethod]
    public async Task Enqueue_VideoQuality_DoesNotCreateMediaStreamEvent()
    {
        await using var fixture = await SchemaFixture.CreateAsync();
        if (fixture is null) return;
        var id = Guid.NewGuid();
        await fixture.ExecuteAsync("INSERT INTO media_jobs (id, kind, status) VALUES (@id, 'video_quality', 'queued')", id);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT queue_version FROM media_jobs WHERE id = @id", id));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT count(*) FROM media_job_outbox WHERE job_id = @id", id));
    }

    private sealed class SchemaFixture(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public static async Task<SchemaFixture?> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("APCS_TEST_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Assert.Inconclusive("Set APCS_TEST_CONNECTION_STRING to an isolated PostgreSQL test database.");
                return null;
            }

            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            var fixture = new SchemaFixture(connection, transaction);
            try
            {
                var schema = "media_stream_test_" + Guid.NewGuid().ToString("N");
                await fixture.ExecuteAsync($"CREATE SCHEMA {schema}; SET LOCAL search_path TO {schema}; CREATE TABLE {schema}.media_jobs (id uuid PRIMARY KEY, kind text NOT NULL, status text NOT NULL, payload text NOT NULL DEFAULT '{{}}', available_at timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'UTC'), attempt integer NOT NULL DEFAULT 0, lease_expires_at timestamp without time zone)");
                var sql = await File.ReadAllTextAsync(FindSchemaScript());
                await fixture.ExecuteAsync(sql.Replace("public.", schema + ".", StringComparison.Ordinal));
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task ExecuteAsync(string sql, Guid? id = null, string? kind = null)
        {
            await using var command = MakeCommand(sql, id, kind);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> ScalarAsync(string sql, Guid? id = null, string? kind = null)
        {
            await using var command = MakeCommand(sql, id, kind);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        private NpgsqlCommand MakeCommand(string sql, Guid? id, string? kind)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            if (id.HasValue) command.Parameters.AddWithValue("id", id.Value);
            if (kind is not null) command.Parameters.AddWithValue("kind", kind);
            return command;
        }

        private static string FindSchemaScript()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "scripts", "sql", "media-streams.sql");
                if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException("Could not locate scripts/sql/media-streams.sql.");
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
