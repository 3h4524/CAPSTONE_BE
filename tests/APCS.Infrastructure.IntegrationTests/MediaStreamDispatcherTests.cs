using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using StackExchange.Redis;

namespace APCS.Infrastructure.IntegrationTests;

[TestClass]
public sealed class MediaStreamDispatcherTests
{
    [TestMethod]
    public async Task Dispatcher_QueuedJob_PublishesAndRepublishesAfterStreamLoss()
    {
        var adminConnection = Environment.GetEnvironmentVariable("APCS_TEST_DISPATCHER_POSTGRES");
        var redisConnection = Environment.GetEnvironmentVariable("APCS_TEST_DISPATCHER_REDIS");
        if (string.IsNullOrWhiteSpace(adminConnection) || string.IsNullOrWhiteSpace(redisConnection))
        {
            Assert.Inconclusive("Set APCS_TEST_DISPATCHER_POSTGRES and APCS_TEST_DISPATCHER_REDIS to isolated test services.");
            return;
        }
        var dbName = "apcs_stream_test_" + Guid.NewGuid().ToString("N");
        var stream = "apcs:test:dispatcher:" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(adminConnection) { Database = dbName, Pooling = false };
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {dbName}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            await using var db = new NpgsqlConnection(builder.ConnectionString);
            await db.OpenAsync();
            var schema = """
                CREATE TABLE public.media_jobs (
                  id uuid PRIMARY KEY, kind text NOT NULL, status text NOT NULL, payload jsonb NOT NULL DEFAULT '{}',
                  available_at timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'UTC'),
                  attempt integer NOT NULL DEFAULT 0, maximum_attempts integer NOT NULL DEFAULT 3,
                  lease_expires_at timestamp without time zone, workflow_run_id uuid NOT NULL DEFAULT gen_random_uuid(),
                  workflow_node_run_id uuid NOT NULL DEFAULT gen_random_uuid(), user_id uuid NOT NULL DEFAULT gen_random_uuid(),
                  lease_token uuid, heartbeat_at timestamp without time zone, progress integer NOT NULL DEFAULT 0,
                  stage text, error_message text, created_at timestamp with time zone NOT NULL DEFAULT now(),
                  updated_at timestamp with time zone NOT NULL DEFAULT now()
                );
                CREATE TABLE public.workflow_runs (id uuid PRIMARY KEY, workflow_id uuid NOT NULL DEFAULT gen_random_uuid(), user_id uuid NOT NULL DEFAULT gen_random_uuid(), product_id uuid NOT NULL DEFAULT gen_random_uuid(), workflow_revision bigint NOT NULL DEFAULT 1, definition_snapshot jsonb NOT NULL DEFAULT '{}', status text NOT NULL, revision bigint NOT NULL DEFAULT 1, idempotency_key text NOT NULL DEFAULT '', request_hash text NOT NULL DEFAULT '', parent_run_id uuid, error_message text, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(), completed_at timestamptz);
                CREATE TABLE public.workflow_node_runs (id uuid PRIMARY KEY, workflow_run_id uuid NOT NULL, node_id text NOT NULL DEFAULT '', node_type text NOT NULL DEFAULT 'export-zip', status text NOT NULL DEFAULT 'running', attempt integer NOT NULL DEFAULT 1, input_snapshot jsonb NOT NULL DEFAULT '{}', output_snapshot jsonb NOT NULL DEFAULT '{}', stage text, progress integer NOT NULL DEFAULT 0, error_message text, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now());
                """;
            await using (var command = new NpgsqlCommand(schema + await File.ReadAllTextAsync(FindSchemaScript()), db)) await command.ExecuteNonQueryAsync();
            var contestedId = Guid.NewGuid();
            await using (var contested = new NpgsqlCommand("INSERT INTO public.media_jobs(id, kind, status) VALUES (@id, 'render', 'queued')", db))
            {
                contested.Parameters.AddWithValue("id", contestedId);
                await contested.ExecuteNonQueryAsync();
            }
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString, o => o.UseVector()).Options;
            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(options))
            {
                await first.Database.OpenConnectionAsync();
                await second.Database.OpenConnectionAsync();
                await using var blocker = new NpgsqlConnection(builder.ConnectionString);
                await blocker.OpenAsync();
                await using var lockTransaction = await blocker.BeginTransactionAsync();
                await using (var rowLock = new NpgsqlCommand("SELECT id FROM public.media_jobs WHERE id = @id FOR UPDATE", blocker, lockTransaction))
                {
                    rowLock.Parameters.AddWithValue("id", contestedId);
                    await rowLock.ExecuteNonQueryAsync();
                }
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var firstToken = Guid.NewGuid(); var secondToken = Guid.NewGuid();
                var now = DateTime.UtcNow.AddSeconds(1);
                async Task<MediaJob?> ClaimAsync(AppDbContext context, Guid token)
                {
                    await start.Task;
                    return await new WorkflowStateRepository(context).ClaimMediaAsync(contestedId, 1, now, token, default);
                }
                var firstClaim = ClaimAsync(first, firstToken);
                var secondClaim = ClaimAsync(second, secondToken);
                start.SetResult();
                await WaitAsync(async () =>
                {
                    await using var waiting = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%UPDATE media_jobs SET status%'", db);
                    return (long)(await waiting.ExecuteScalarAsync())! == 2;
                });
                await lockTransaction.CommitAsync();
                var claims = await Task.WhenAll(firstClaim, secondClaim);
                Assert.AreEqual(1, claims.Count(claim => claim is not null));
                var winner = claims.Single(claim => claim is not null)!;
                Assert.AreEqual(1, winner.Attempt);
                Assert.AreEqual(winner.LeaseToken, ReferenceEquals(winner, claims[0]) ? firstToken : secondToken);
                await using var check = new NpgsqlCommand("SELECT attempt, lease_token FROM public.media_jobs WHERE id = @id", db);
                check.Parameters.AddWithValue("id", contestedId);
                await using var reader = await check.ExecuteReaderAsync();
                Assert.IsTrue(await reader.ReadAsync());
                Assert.AreEqual(1, reader.GetInt32(0));
                Assert.AreEqual(winner.LeaseToken, reader.GetGuid(1));
            }
            await using (var cleanup = new NpgsqlCommand("DELETE FROM public.media_jobs WHERE id = @id", db))
            {
                cleanup.Parameters.AddWithValue("id", contestedId);
                await cleanup.ExecuteNonQueryAsync();
            }
            var id = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("INSERT INTO public.media_jobs(id, kind, status) VALUES (@id, 'render', 'queued')", db))
            {
                insert.Parameters.AddWithValue("id", id);
                await insert.ExecuteNonQueryAsync();
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MediaQueue:Mode"] = "streams", ["ConnectionStrings:DefaultConnection"] = builder.ConnectionString,
                ["MediaQueue:RedisConnectionString"] = redisConnection, ["MediaQueue:Stream"] = stream
            }).Build();
            var registrations = new ServiceCollection().AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.ConnectionString, o => o.UseVector()));
            registrations.AddScoped<IUnitOfWork>(p => p.GetRequiredService<AppDbContext>());
            registrations.AddScoped<IWorkflowStateRepository, WorkflowStateRepository>();
            registrations.AddScoped(p => new WorkflowRuntime(new Repository<WorkflowNodeRun>(p.GetRequiredService<AppDbContext>()), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, TimeProvider.System));
            registrations.AddScoped<IMediaLeaseRecovery, MediaLeaseRecovery>();
            var services = registrations.BuildServiceProvider();
            await using var provider = services;
            using var redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
            var redisDb = redis.GetDatabase();
            try
            {
                using (var dispatcher = Start(provider, configuration))
                {
                    await dispatcher.StartAsync(default);
                    await WaitAsync(async () => await redisDb.StreamLengthAsync(stream) == 1);
                    var entry = (await redisDb.StreamRangeAsync(stream)).Single();
                    Assert.AreEqual(id.ToString("D"), entry.Values.Single(x => x.Name == "jobId").Value.ToString());
                    Assert.AreEqual("1", entry.Values.Single(x => x.Name == "queueVersion").Value.ToString());
                    await WaitAsync(async () => await PublishedAsync(db));
                    await dispatcher.StopAsync(default);
                }
                await redisDb.KeyDeleteAsync(stream);
                await using (var age = new NpgsqlCommand("UPDATE public.media_job_outbox SET published_at = (now() AT TIME ZONE 'UTC') - interval '3 minutes'", db)) await age.ExecuteNonQueryAsync();
                using (var dispatcher = Start(provider, configuration))
                {
                    await dispatcher.StartAsync(default);
                    await WaitAsync(async () => await redisDb.StreamLengthAsync(stream) == 1);
                    var recovered = (await redisDb.StreamRangeAsync(stream)).Single();
                    Assert.AreEqual(id.ToString("D"), recovered.Values.Single(x => x.Name == "jobId").Value.ToString());
                    Assert.AreEqual("1", recovered.Values.Single(x => x.Name == "queueVersion").Value.ToString());
                    await WaitAsync(async () => await PublishedAsync(db));
                    await dispatcher.StopAsync(default);
                }
                var retryId = Guid.NewGuid();
                var failedId = Guid.NewGuid();
                var retryRun = Guid.NewGuid();
                var failedRun = Guid.NewGuid();
                var retryNode = Guid.NewGuid();
                var failedNode = Guid.NewGuid();
                await using (var expired = new NpgsqlCommand("""
                    INSERT INTO public.workflow_runs(id, status) VALUES (@retryRun, 'running'), (@failedRun, 'running');
                    INSERT INTO public.workflow_node_runs(id, workflow_run_id) VALUES (@retryNode, @retryRun), (@failedNode, @failedRun);
                    INSERT INTO public.media_jobs(id, kind, status, attempt, maximum_attempts, workflow_run_id, workflow_node_run_id, queue_version, lease_token, lease_expires_at)
                    VALUES (@retry, 'render', 'leased', 1, 3, @retryRun, @retryNode, 1, gen_random_uuid(), now() - interval '1 minute'),
                           (@failed, 'export_zip', 'leased', 3, 3, @failedRun, @failedNode, 1, gen_random_uuid(), now() - interval '1 minute');
                    """, db))
                {
                    expired.Parameters.AddWithValue("retry", retryId); expired.Parameters.AddWithValue("failed", failedId);
                    expired.Parameters.AddWithValue("retryRun", retryRun); expired.Parameters.AddWithValue("failedRun", failedRun);
                    expired.Parameters.AddWithValue("retryNode", retryNode); expired.Parameters.AddWithValue("failedNode", failedNode);
                    await expired.ExecuteNonQueryAsync();
                }
                using (var dispatcher = Start(provider, configuration))
                {
                    await dispatcher.StartAsync(default);
                    await WaitAsync(async () =>
                    {
                        await using var check = new NpgsqlCommand("SELECT count(*) FROM public.media_jobs WHERE (id = @retry AND status = 'queued' AND queue_version = 2) OR (id = @failed AND status = 'failed')", db);
                        check.Parameters.AddWithValue("retry", retryId); check.Parameters.AddWithValue("failed", failedId);
                        return (long)(await check.ExecuteScalarAsync())! == 2;
                    });
                    await using var verify = new NpgsqlCommand("SELECT (SELECT status FROM public.workflow_runs WHERE id = @run), (SELECT status FROM public.workflow_node_runs WHERE id = @node), (SELECT count(*) FROM public.media_job_outbox WHERE job_id = @retry AND queue_version = 2)", db);
                    verify.Parameters.AddWithValue("run", failedRun); verify.Parameters.AddWithValue("node", failedNode); verify.Parameters.AddWithValue("retry", retryId);
                    await using var reader = await verify.ExecuteReaderAsync();
                    Assert.IsTrue(await reader.ReadAsync());
                    Assert.AreEqual("failed", reader.GetString(0)); Assert.AreEqual("failed", reader.GetString(1)); Assert.AreEqual(1L, reader.GetInt64(2));
                    await dispatcher.StopAsync(default);
                }
            }
            finally { await redisDb.KeyDeleteAsync(stream); }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {dbName} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static MediaStreamDispatcher Start(ServiceProvider provider, IConfiguration configuration) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), configuration, new TestLogger());

    private sealed class TestLogger : ILogger<MediaStreamDispatcher>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Console.WriteLine($"{level}: {formatter(state, exception)} {exception}");
    }

    private static async Task<bool> PublishedAsync(NpgsqlConnection db)
    {
        await using var command = new NpgsqlCommand("SELECT published_at IS NOT NULL FROM public.media_job_outbox", db);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail("Timed out waiting for dispatcher to publish the outbox event.");
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
}
