MEDIA_WORKER_API_KEY=${MEDIA_WORKER_API_KEY}
AI_SCENE_CONCURRENCY=1
# Scenes of one export rendered at the same time (1-4). Unset: what fits the host's cores at two tabs a scene, at most 2, so e2-standard-2 keeps one.
# EXPORT_SCENE_CONCURRENCY=1
AI_SHOT_FIRST_POLL_SECONDS=30
AI_SHOT_POLL_SECONDS=10
AI_SHOT_TIMEOUT_MINUTES=25
MEDIA_QUEUE_MODE=streams
MEDIA_QUEUE_REDIS_URL=redis://default:${REDIS_MEDIA_PASSWORD}@redis-media:6379
MEDIA_QUEUE_STREAM=apcs:prod:media:jobs
