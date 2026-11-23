ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

ConnectionStrings__DefaultConnection=Host=postgres-prod;Port=5432;Database=apcs;Username=apcs_app;Password=${APCS_APP_PASSWORD};Maximum Pool Size=40
Redis__ConnectionString=redis:6379,password=${REDIS_PASSWORD},abortConnect=false
Redis__InstanceName=APCS:
Redis__DefaultExpirationMinutes=10

Jwt__Issuer=APCS
Jwt__Audience=APCS.Web
Jwt__SigningKey=${JWT_SIGNING_KEY}
Jwt__AccessTokenMinutes=15
Jwt__RefreshTokenDays=14

App__BaseUrl=https://${APP_DOMAIN}
App__VerifyEmailPath=/verify-email
App__ResetPasswordPath=/reset-password
Cors__AllowedOrigins=https://${APP_DOMAIN}

ApiKeys__KeyRingPath=/data/keyring
Mockups__SegmentationModelPath=/app/models/birefnet-lite-cpu.onnx
Mockups__ClothesModelPath=/app/models/segformer-b2-clothes-quantized.onnx

RateLimiting__Enabled=true
RateLimiting__UserPermitsPerMinute=600
RateLimiting__AnonymousPermitsPerMinute=300
RateLimiting__AuthPermitsPerMinute=20
RateLimiting__OtpPermitsPerMinute=5
RateLimiting__AssistantChatPermitsPerMinute=20

MediaWorker__ApiKey=${MEDIA_WORKER_API_KEY}
MediaQueue__Mode=streams
MediaQueue__RedisConnectionString=redis-media:6379,password=${REDIS_MEDIA_PASSWORD},abortConnect=false
MediaQueue__Stream=apcs:prod:media:jobs

Authentication__Google__ClientId=${GOOGLE_CLIENT_ID}

Smtp__Host=${SMTP_HOST}
Smtp__Port=587
Smtp__UseStartTls=true
Smtp__Username=${SMTP_USERNAME}
Smtp__Password=${SMTP_PASSWORD}
Smtp__FromAddress=${SMTP_FROM_ADDRESS}
Smtp__FromName=APCS

Cloudinary__CloudName=${CLOUDINARY_CLOUD_NAME}
Cloudinary__ApiKey=${CLOUDINARY_API_KEY}
Cloudinary__ApiSecret=${CLOUDINARY_API_SECRET}
Cloudinary__Folder=apcs
Cloudinary__SignedUrlTtlMinutes=10

PayOS__ClientId=${PAYOS_CLIENT_ID}
PayOS__ApiKey=${PAYOS_API_KEY}
PayOS__ChecksumKey=${PAYOS_CHECKSUM_KEY}
PayOS__UsdToVndRate=25000
PayOS__TestAmountVnd=2000

Gemini__Embeddings__ApiKey=${GEMINI_API_KEY}
Gemini__TextGeneration__ApiKey=${GEMINI_API_KEY}
Assistant__ApiKeys=${ASSISTANT_API_KEYS}
Assistant__Model=gemini-3.5-flash-lite
Gemini__TextGeneration__Model=gemini-3.5-flash-lite
Gemini__TextGeneration__FallbackModel=gemini-3.5-flash-lite
PlanAiReview__Model=gemini-3.5-flash-lite
PlanAiReview__FallbackModel=gemini-3.5-flash-lite
Etsy__ApiKey=${ETSY_API_KEY}
Etsy__SharedSecret=${ETSY_SHARED_SECRET}
Etsy__OAuthRedirectUri=https://${APP_DOMAIN}/api/etsy/oauth/callback
Translation__ApiKey=${TRANSLATION_API_KEY}
Printify__CatalogToken=${PRINTIFY_CATALOG_TOKEN}

# The platform Ideogram and MiniMax keys are kept by an Admin at /admin/system-api-keys (in the database), not here.

# The API makes the AI scenes of a render job before the media worker gets it (docs/VIDEO_WORKFLOWS.md, "AI scenes made by the API").
# Keep MaxInFlight within what the MiniMax account allows at once; the media worker then only renders.
AiVideo__ClipPhase__Enabled=true
AiVideo__ClipPhase__MaxInFlight=3
AiVideo__ClipPhase__MaxInFlightPerUser=2

# Putting a mock-up on a model photo without AI. Off, a model photo is only stored and is dressed with AI.
ModelPhotos__PrintWithoutAi=false

DesignTextCheck__Enabled=false
VideoQuality__Enabled=false
VideoQuality__AutoEvaluate=false
MarketResearch__Scheduled__Enabled=false
