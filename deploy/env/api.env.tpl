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
Cloudinary__Folder=apcs-prod
Cloudinary__SignedUrlTtlMinutes=10

PayOS__ClientId=${PAYOS_CLIENT_ID}
PayOS__ApiKey=${PAYOS_API_KEY}
PayOS__ChecksumKey=${PAYOS_CHECKSUM_KEY}
PayOS__UsdToVndRate=25000
PayOS__TestAmountVnd=0

Gemini__Embeddings__ApiKey=${GEMINI_API_KEY}
Gemini__TextGeneration__ApiKey=${GEMINI_API_KEY}
Assistant__ApiKeys=${ASSISTANT_API_KEYS}
Assistant__Model=gemini-3.5-flash-lite
Gemini__TextGeneration__Model=gemini-3.5-flash-lite
Gemini__TextGeneration__FallbackModel=gemini-3.5-flash-lite
PlanAiReview__Model=gemini-3.5-flash-lite
PlanAiReview__FallbackModel=gemini-3.5-flash-lite
MiniMax__ApiKey=${MINIMAX_API_KEY}
Etsy__ApiKey=${ETSY_API_KEY}
Etsy__SharedSecret=${ETSY_SHARED_SECRET}
Etsy__OAuthRedirectUri=https://api.${APP_DOMAIN}/etsy/oauth/callback
Translation__ApiKey=${TRANSLATION_API_KEY}

DesignTextCheck__Enabled=false
VideoQuality__Enabled=false
MarketResearch__Scheduled__Enabled=false
