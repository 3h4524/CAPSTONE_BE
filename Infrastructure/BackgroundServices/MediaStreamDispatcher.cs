using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using System.Diagnostics.Metrics;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>Dispatches committed media outbox records and recovers expired leases.</summary>
/// <remarks>
/// With an <see cref="WorkflowActivitySignal"/> it goes idle once no outbox record is pending and no render or export
/// job is in flight: it closes its connections and waits for <see cref="WorkflowActivitySignal.Media"/>, so the
/// database is not queried and can scale to zero. Scaling to zero would end a <c>LISTEN</c> session anyway, and the
/// reconnect would wake the database again. Without a signal (tests) it never goes idle.
/// </remarks>
public sealed class MediaStreamDispatcher(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<MediaStreamDispatcher> logger, WorkflowActivitySignal? activity = null) : BackgroundService
{

    private sealed record Outbox(Guid EventId, Guid JobId, long Version, string Kind, int Attempts, Guid Token);
    private static readonly Meter Meter = new("APCS.MediaQueue", "1.0.0");
    private static readonly Counter<long> Published = Meter.CreateCounter<long>("media_queue_published");
    private static readonly Counter<long> PublishFailures = Meter.CreateCounter<long>("media_queue_publish_failures");
    private static readonly Counter<long> RecoveredLeases = Meter.CreateCounter<long>("media_queue_expired_leases");
    private static long _outboxPending, _oldestSeconds, _queuedSeconds, _streamLength, _streamPending;
    private static readonly ObservableGauge<long> OutboxPending = Meter.CreateObservableGauge("media_queue_outbox_pending", () => Interlocked.Read(ref _outboxPending));
    private static readonly ObservableGauge<long> OldestOutbox = Meter.CreateObservableGauge("media_queue_oldest_outbox_seconds", () => Interlocked.Read(ref _oldestSeconds));
    private static readonly ObservableGauge<long> OldestQueued = Meter.CreateObservableGauge("media_queue_oldest_queued_seconds", () => Interlocked.Read(ref _queuedSeconds));
    private static readonly ObservableGauge<long> StreamLength = Meter.CreateObservableGauge("media_queue_stream_length", () => Interlocked.Read(ref _streamLength));
    private static readonly ObservableGauge<long> StreamPending = Meter.CreateObservableGauge("media_queue_stream_pending", () => Interlocked.Read(ref _streamPending));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mode = configuration["MediaQueue:Mode"] ?? "polling";
        if (string.Equals(mode, "polling", StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(mode, "streams", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MediaQueue:Mode must be polling or streams.");
        var postgres = configuration["MediaQueue:ListenerConnectionString"] ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Media queue requires a PostgreSQL listener connection.");
        var redisConfig = configuration["MediaQueue:RedisConnectionString"];
        var stream = configuration["MediaQueue:Stream"];
        if (string.IsNullOrWhiteSpace(redisConfig) || string.IsNullOrWhiteSpace(stream))
            throw new InvalidOperationException("MediaQueue:RedisConnectionString and MediaQueue:Stream are required in streams mode.");

        WarnWhenListeningThroughPooler(postgres);
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            long? idleSince = null;
            try
            {
                await using var listener = new NpgsqlConnection(postgres);
                await listener.OpenAsync(stoppingToken);
                await using (var command = new NpgsqlCommand("LISTEN apcs_media_outbox", listener))
                    await command.ExecuteNonQueryAsync(stoppingToken);
                using var redis = await ConnectionMultiplexer.ConnectAsync(redisConfig);
                failures = 0;
                var nextRecovery = DateTime.MinValue;
                while (!stoppingToken.IsCancellationRequested)
                {
                    var seen = activity?.Media.Version ?? 0;
                    if (DateTime.UtcNow >= nextRecovery)
                    {
                        await RecoverAsync(stoppingToken);
                        await ObserveAsync(redis.GetDatabase(), stream, stoppingToken);
                        nextRecovery = DateTime.UtcNow.AddSeconds(30);
                    }
                    var count = await DispatchAsync(redis.GetDatabase(), stream, stoppingToken);
                    if (count == 100) continue;
                    var (delay, outboxPending) = await NextDelayAsync(nextRecovery, stoppingToken);
                    // The signal is raised after the commit, so a job it announced is already visible to these queries.
                    if (activity is not null && !outboxPending && !await MediaWorkInFlightAsync(stoppingToken))
                    {
                        idleSince = seen;
                        break;
                    }
                    await listener.WaitAsync(delay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Media stream dispatcher unavailable; retrying.");
                failures++;
                var seconds = Math.Min(30, Math.Pow(2, Math.Min(failures, 5)) + Random.Shared.NextDouble());
                await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
                continue;
            }

            if (activity is null || idleSince is not { } idleVersion) continue;
            // The connections are closed: nothing queries the database until a job is queued or the safety check is due.
            logger.LogInformation("Media stream dispatcher idle: no outbox record or media job in flight.");
            try
            {
                await activity.Media.WaitAsync(idleVersion, WorkflowActivitySignal.IdleSafetyCheck, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task<int> DispatchAsync(IDatabase redis, string stream, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        var token = Guid.NewGuid();
        const string sql = """
            WITH candidate AS (
              SELECT event_id FROM public.media_job_outbox
              WHERE published_at IS NULL AND next_attempt_at <= (now() AT TIME ZONE 'UTC')
                AND (claim_until IS NULL OR claim_until < (now() AT TIME ZONE 'UTC'))
              ORDER BY next_attempt_at FOR UPDATE SKIP LOCKED LIMIT 100
            ) UPDATE public.media_job_outbox o SET claim_token = @token,
              claim_until = (now() AT TIME ZONE 'UTC') + interval '2 minutes', attempts = attempts + 1
              FROM candidate c WHERE o.event_id = c.event_id
              RETURNING o.event_id, o.job_id, o.queue_version, o.kind, o.attempts
            """;
        var events = new List<Outbox>();
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("token", token);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) events.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetString(3), reader.GetInt32(4), token));
        }
        foreach (var item in events)
        {
            try
            {
                await redis.StreamAddAsync(stream, [new("schemaVersion", "1"), new("eventId", item.EventId.ToString("D")),
                    new("jobId", item.JobId.ToString("D")), new("kind", item.Kind), new("queueVersion", item.Version)]);
                await using var done = new NpgsqlCommand("UPDATE public.media_job_outbox SET published_at = (now() AT TIME ZONE 'UTC'), claim_token = NULL, claim_until = NULL WHERE event_id = @id AND claim_token = @token", connection);
                done.Parameters.AddWithValue("id", item.EventId); done.Parameters.AddWithValue("token", item.Token);
                await done.ExecuteNonQueryAsync(ct);
                Published.Add(1);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not publish media outbox event {EventId}.", item.EventId);
                PublishFailures.Add(1);
                var seconds = Math.Min(30, Math.Pow(2, Math.Min(item.Attempts, 5)) + Random.Shared.NextDouble());
                // A broker outage affects the whole reserved batch. Release unsent rows together
                // instead of waiting for a separate network timeout for each remaining event.
                await using var retry = new NpgsqlCommand("UPDATE public.media_job_outbox SET claim_token = NULL, claim_until = NULL, next_attempt_at = (now() AT TIME ZONE 'UTC') + @delay WHERE published_at IS NULL AND claim_token = @token", connection);
                retry.Parameters.AddWithValue("delay", TimeSpan.FromSeconds(seconds)); retry.Parameters.AddWithValue("token", item.Token);
                await retry.ExecuteNonQueryAsync(ct);
                break;
            }
        }
        return events.Count;
    }

    /// <summary>
    /// A transaction pooler (PgBouncer in transaction mode, such as Neon's <c>-pooler</c> host) does not deliver
    /// <c>LISTEN</c> notifications: jobs from other processes would then wait for the 30-second pass. Say so.
    /// </summary>
    private void WarnWhenListeningThroughPooler(string connectionString)
    {
        string? host;
        try { host = new NpgsqlConnectionStringBuilder(connectionString).Host; }
        catch (ArgumentException) { return; }
        if (host is null || !host.Contains("-pooler", StringComparison.OrdinalIgnoreCase)) return;
        logger.LogWarning(
            "Media stream dispatcher listens through the connection pooler host {Host}, which does not deliver LISTEN notifications. " +
            "Set MediaQueue__ListenerConnectionString to a direct connection (the same host without -pooler).", host);
    }

    /// <summary>How long to wait before the next pass, and whether any outbox record is still unpublished.</summary>
    private async Task<(TimeSpan Delay, bool OutboxPending)> NextDelayAsync(DateTime nextRecovery, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var next = await db.Database.SqlQuery<DateTime?>($"SELECT MIN(GREATEST(next_attempt_at, COALESCE(claim_until, next_attempt_at))) AS \"Value\" FROM public.media_job_outbox WHERE published_at IS NULL").SingleAsync(ct);
        var target = next.HasValue && next.Value < nextRecovery ? next.Value : nextRecovery;
        return (TimeSpan.FromMilliseconds(Math.Clamp((target - DateTime.UtcNow).TotalMilliseconds, 100, 30000)), next.HasValue);
    }

    /// <summary>Whether a queued or leased job could still need a republish or a lease recovery.</summary>
    private async Task<bool> MediaWorkInFlightAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<IWorkflowStateRepository>();
        return await state.HasMediaWorkInFlightAsync(DateTime.UtcNow - WorkflowActivitySignal.InFlightWindow, ct);
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended('apcs-media-recovery', 0))", connection);
        if (await acquire.ExecuteScalarAsync(ct) is not true) return;
        try
        {
        var now = DateTime.UtcNow;
        // A queued event may have disappeared after Redis lost data. Republishing the same generation is safe.
        await db.Database.ExecuteSqlInterpolatedAsync($"WITH stale AS (SELECT o.event_id FROM public.media_job_outbox o JOIN public.media_jobs j ON o.job_id = j.id AND o.queue_version = j.queue_version WHERE j.kind IN ('render', 'export_zip') AND j.status = 'queued' AND j.available_at <= {now} AND o.published_at < {now.AddMinutes(-2)} ORDER BY o.published_at LIMIT 100 FOR UPDATE OF o SKIP LOCKED) UPDATE public.media_job_outbox o SET published_at = NULL, next_attempt_at = {now} FROM stale WHERE o.event_id = stale.event_id", ct);
        // Expired leases are settled with their runs in one place, shared with the polling claim.
        using (var recoveryScope = scopes.CreateScope())
            RecoveredLeases.Add(await recoveryScope.ServiceProvider.GetRequiredService<IMediaLeaseRecovery>().RecoverExpiredAsync(now, ct));
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended('apcs-media-recovery', 0))", connection);
            await release.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    private async Task ObserveAsync(IDatabase redis, string stream, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT (SELECT count(*) FROM public.media_job_outbox WHERE published_at IS NULL),
              COALESCE((SELECT EXTRACT(EPOCH FROM ((now() AT TIME ZONE 'UTC') - MIN(created_at)))::bigint
                FROM public.media_job_outbox WHERE published_at IS NULL AND available_at <= (now() AT TIME ZONE 'UTC')), 0),
              COALESCE((SELECT EXTRACT(EPOCH FROM ((now() AT TIME ZONE 'UTC') - MIN(available_at)))::bigint
                FROM public.media_jobs WHERE kind IN ('render', 'export_zip') AND status = 'queued'
                AND available_at <= (now() AT TIME ZONE 'UTC')), 0)
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            Interlocked.Exchange(ref _outboxPending, reader.GetInt64(0));
            Interlocked.Exchange(ref _oldestSeconds, reader.GetInt64(1));
            Interlocked.Exchange(ref _queuedSeconds, reader.GetInt64(2));
        }
        var length = await redis.StreamLengthAsync(stream);
        long pending = 0;
        try { pending = (await redis.StreamPendingAsync(stream, "media-workers")).PendingMessageCount; }
        catch (RedisServerException exception) when (exception.Message.Contains("NOGROUP", StringComparison.Ordinal)) { }
        Interlocked.Exchange(ref _streamLength, length);
        Interlocked.Exchange(ref _streamPending, pending);
        logger.LogInformation("Media queue: outbox {OutboxPending}, oldest outbox {OldestOutboxSeconds}s, oldest queued {OldestQueuedSeconds}s, stream {StreamLength}, pending {StreamPending}.",
            Interlocked.Read(ref _outboxPending), Interlocked.Read(ref _oldestSeconds), Interlocked.Read(ref _queuedSeconds), length, pending);
    }
}
