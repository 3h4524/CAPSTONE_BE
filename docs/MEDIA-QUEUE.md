# Media job queue

Render and ZIP export jobs can use Redis Streams. PostgreSQL remains authoritative for job state and leases; `video_quality` continues using its own existing worker.

## Local setup

1. Prepare a disposable PostgreSQL database with the application's schema. Review and apply `scripts/sql/media-streams.sql` to that database. It adds an outbox and triggers; it does not change a database until explicitly run. The script backfills currently queued media jobs. Never run it against the shared Neon database merely to test locally.
2. Start the dedicated local Redis using `docker compose -f media-worker/compose.yml up -d redis-media`. Its volume persists data, AOF is enabled with `everysec`, and its memory policy is `noeviction`.
3. Configure the API with `MediaQueue__Mode=streams`, `MediaQueue__RedisConnectionString=localhost:6380,abortConnect=false`, and `MediaQueue__Stream=apcs:dev:media:jobs`. Set `MediaQueue__ListenerConnectionString` to a direct PostgreSQL connection suitable for a long-lived `LISTEN` session when the normal connection goes through a transaction pooler.
4. Configure the worker with `MEDIA_QUEUE_MODE=streams`, `MEDIA_QUEUE_REDIS_URL=redis://localhost:6380`, and the same `MEDIA_QUEUE_STREAM`. A worker in the Compose network uses `redis://redis-media:6379`. `scripts/Start-MediaWorker.ps1` also reads these settings from the ignored root `.env`.
5. Start the API and worker. Defaults remain `polling` for both until explicitly changed. Both processes must use the same mode. In streams mode the legacy claim endpoint returns a conflict, and Redis outages never switch the worker back to polling.

For production, provision a dedicated Redis with TLS, ACLs, appropriate persistent storage, monitoring and memory sizing. Use an environment-specific stream key. AOF `everysec` can lose the latest writes on a crash; queued jobs are reconciled from PostgreSQL after two minutes. Redis is not the system of record.

## Deployment and recovery

Apply the reviewed SQL to the intended database using the repository's database-first workflow, regenerate the database model against that schema, and deploy the API in polling mode. Drain existing workers, set the API and worker to streams mode, and start the new workers. The script creates outbox records for existing queued media jobs. To roll back, stop the Streams consumers and dispatcher by changing the mode to polling before restarting old workers. Keep the outbox schema in place during rollback.

Job status and lease tokens fence duplicate stream messages. The dispatcher uses a dedicated PostgreSQL `LISTEN` session for low-latency notification, and a 30-second reconciliation for missed notifications, delayed messages and expired leases. Published messages are acknowledged and deleted only after the API reports a durable result or a message is known obsolete; pending messages can be reclaimed. Jobs are recovered from PostgreSQL after Redis data loss. A failed Redis connection is retried and is never treated as a successful publish.

Each API instance emits structured `Media queue:` logs for outbox count, oldest due outbox age, oldest queued job age, stream length and pending count. The `APCS.MediaQueue` meter also exposes these gauges and publish, publish-failure and expired-lease counters. Alert on increasing age or pending count rather than an individual retry. Invalid messages go to `<stream>:dead`; inspect and remove them according to operational retention policy.
