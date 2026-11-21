using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;

namespace APCS.Infrastructure.IntegrationTests;

[TestClass]
public sealed class RunGroupExportBarrierTests
{
    [TestMethod]
    public async Task ClaimQueuedGroupRunAsync_ExportsWaitForEveryReviewAndSkipLockedRuns()
    {
        var adminConnection = Environment.GetEnvironmentVariable("APCS_TEST_DISPATCHER_POSTGRES");
        if (string.IsNullOrWhiteSpace(adminConnection))
        {
            Assert.Inconclusive("Set APCS_TEST_DISPATCHER_POSTGRES to an isolated PostgreSQL service.");
            return;
        }
        var database = "apcs_export_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database, Pooling = false }.ConnectionString;
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection, o => o.UseVector()).Options;
            await using var db = new AppDbContext(options);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE public.workflow_runs (
                  id uuid PRIMARY KEY, workflow_id uuid NOT NULL DEFAULT gen_random_uuid(), user_id uuid NOT NULL DEFAULT gen_random_uuid(),
                  product_id uuid NOT NULL DEFAULT gen_random_uuid(), workflow_revision bigint NOT NULL DEFAULT 1,
                  definition_snapshot jsonb NOT NULL DEFAULT '{{}}', status text NOT NULL, revision bigint NOT NULL DEFAULT 1,
                  idempotency_key text NOT NULL DEFAULT '', request_hash text NOT NULL DEFAULT '', parent_run_id uuid,
                  run_group_id uuid, artwork_group_key text, error_message text, created_at timestamptz NOT NULL DEFAULT now(),
                  updated_at timestamptz NOT NULL DEFAULT now(), completed_at timestamptz);
                CREATE TABLE public.workflow_node_runs (workflow_run_id uuid NOT NULL, node_type text NOT NULL, status text NOT NULL);
                """);
            var group = Guid.NewGuid();
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.workflow_runs(id, status, run_group_id, created_at) VALUES ({first}, 'running', {group}, now() - interval '1 minute'), ({second}, 'queued', {group}, now())");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.workflow_node_runs(workflow_run_id, node_type, status) VALUES ({first}, 'generate-video', 'succeeded'), ({first}, 'review-video', 'succeeded'), ({first}, 'export-zip', 'pending'), ({second}, 'generate-video', 'pending'), ({second}, 'review-video', 'pending'), ({second}, 'export-zip', 'pending')");
            var repository = new WorkflowStateRepository(db);

            // The waiting export is older, but the unstarted video must be claimed first.
            Assert.AreEqual(second, (await repository.ClaimQueuedGroupRunAsync(default))?.Id);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.workflow_runs SET status = 'running' WHERE id = {second}");
            db.ChangeTracker.Clear();
            Assert.IsNull(await repository.ClaimQueuedGroupRunAsync(default));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.workflow_node_runs SET status = 'succeeded' WHERE workflow_run_id = {second} AND node_type = 'generate-video'");
            Assert.IsNull(await repository.ClaimQueuedGroupRunAsync(default), "A finished render still waits for review.");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.workflow_node_runs SET status = 'succeeded' WHERE workflow_run_id = {second} AND node_type = 'review-video'");

            await using var transaction = await db.Database.BeginTransactionAsync();
            Assert.AreEqual(first, (await repository.ClaimQueuedGroupRunAsync(default))?.Id);
            await using var other = new AppDbContext(options);
            await using var otherTransaction = await other.Database.BeginTransactionAsync();
            Assert.AreEqual(second, (await new WorkflowStateRepository(other).ClaimQueuedGroupRunAsync(default))?.Id,
                "Another planner must skip the export already held by the first planner.");
            await otherTransaction.RollbackAsync();
            await transaction.RollbackAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE public.workflow_node_runs SET status = 'running' WHERE node_type = 'export-zip'");
            Assert.IsNull(await repository.ClaimQueuedGroupRunAsync(default), "Started exports must not be queued again.");
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
