using System.Text;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Abstractions.VideoQuality;
using APCS.Application.Abstractions;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Translation;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.DesignGeneration.Common;
using APCS.Application.Features.Commerce;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Common.Constants;
using APCS.Common.Extensions;
using APCS.Domain.Entities;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using APCS.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;

namespace APCS.Infrastructure;

/// <summary>
/// Registers infrastructure-layer services.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds persistence, account, JWT, and infrastructure adapters.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetRequiredConfigurationSection(ConfigurationSections.Jwt))
            .ValidateDataAnnotations()
            .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32, "JWT signing key must be at least 32 bytes.")
            .ValidateOnStart();

        // Not required/ValidateOnStart: Google Sign-In is optional, and an environment that
        // hasn't set up OAuth credentials yet should still boot. GoogleAuthService rejects every
        // token instead when the client ID is missing.
        services.AddOptions<GoogleAuthOptions>()
            .Bind(configuration.GetSection(ConfigurationSections.GoogleAuth));

        services.AddOptions<AppOptions>()
            .Bind(configuration.GetRequiredConfigurationSection(ConfigurationSections.App))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Not required/ValidateOnStart: an environment with no mail server should still boot,
        // and EmailService logs the message instead of sending it when the host is missing.
        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(ConfigurationSections.Smtp));

        // Not required/ValidateOnStart: a dev box without a PayOS payment channel yet should
        // still boot. PayOsGatewayClient is only usable once real keys are set.
        services.AddOptions<PayOsOptions>()
            .Bind(configuration.GetSection(ConfigurationSections.PayOs));

        // Projects PayOsOptions down to the narrow settings shape SubscriptionService (Application)
        // is allowed to depend on, so Application never references an Infrastructure options type.
        services.AddSingleton<IOptions<PaymentGatewaySettings>>(provider =>
        {
            var payOsOptions = provider.GetRequiredService<IOptions<PayOsOptions>>().Value;
            return Microsoft.Extensions.Options.Options.Create(
                new PaymentGatewaySettings(payOsOptions.UsdToVndRate, payOsOptions.TestAmountVnd));
        });

        services.AddOptions<CloudinaryOptions>()
            .Bind(configuration.GetSection(ConfigurationSections.Cloudinary));

        // Not required/ValidateOnStart: the test reports are a developer tool, and a box that
        // never runs the suite still boots.
        services.AddOptions<TestReportOptions>()
            .Bind(configuration.GetSection(TestReportOptions.SectionName));
        services.AddScoped<ITestReportStore, DatabaseTestReportStore>();
        services.AddSingleton<ITestReportRunner, ProcessTestReportRunner>();

        // Not required/ValidateOnStart: the Gemini key itself is per-Seller BYOK (ApiKeys table),
        // not a platform credential, so a box with no override here still boots on the defaults.
        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(ConfigurationSections.Gemini));
        services.AddSingleton<IOptions<DesignGenerationSettings>>(provider =>
        {
            var gemini = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var ideogram = provider.GetRequiredService<IOptions<IdeogramOptions>>().Value;
            return Microsoft.Extensions.Options.Options.Create(new DesignGenerationSettings(Math.Max(1, gemini.MaxConcurrentImages))
            {
                MaxConcurrentImagesByProvider = new Dictionary<string, int>
                {
                    [ImageGenerationProviders.Ideogram] = Math.Max(1, ideogram.MaxConcurrentImages)
                }
            });
        });
        services.AddHttpClient("Gemini", client => client.Timeout = TimeSpan.FromSeconds(120))
            .RedactLoggedHeaders(new[] { "x-goog-api-key" });
        services.AddOptions<IdeogramOptions>()
            .Bind(configuration.GetSection(ConfigurationSections.Ideogram));
        services.AddHttpClient("Ideogram", client => client.Timeout = TimeSpan.FromSeconds(120))
            .RedactLoggedHeaders(new[] { "Api-Key" });
        // Every service takes all the image providers and picks one by name: design generation the one its job names,
        // lifestyle mock-ups Ideogram (precise edit) and AI Studio frames Gemini.
        services.AddScoped<IImageGenerationProvider, GeminiImageProvider>();
        services.AddScoped<IImageGenerationProvider, IdeogramImageProvider>();
        services.AddHttpClient("Veo", client => client.Timeout = TimeSpan.FromMinutes(3))
            .RedactLoggedHeaders(new[] { "x-goog-api-key" })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("MiniMax", client => client.Timeout = TimeSpan.FromMinutes(3))
            .RedactLoggedHeaders(new[] { "Authorization" })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        // The default AI-video model is platform-funded. Keep the API key in environment-backed
        // configuration rather than asking each Seller to connect a MiniMax account.
        services.AddOptions<MiniMaxOptions>()
            .Bind(configuration.GetSection(MiniMaxOptions.SectionName));
        services.AddScoped<IAiVideoCredentialResolver, AiVideoCredentialResolver>();
        services.AddScoped<IVeoVideoProvider, VeoVideoProvider>();
        services.AddScoped<IAiVideoGenerationStrategy>(provider => provider.GetRequiredService<IVeoVideoProvider>() as IAiVideoGenerationStrategy
            ?? throw new InvalidOperationException("The registered Veo provider must implement an AI video strategy."));
        services.AddScoped<IAiVideoGenerationStrategy, MiniMaxVideoProvider>();
        services.AddScoped<IAiVideoGenerationStrategy>(provider => new MiniMaxVideoProvider(
            provider.GetRequiredService<IHttpClientFactory>(), provider.GetRequiredService<ILogger<MiniMaxVideoProvider>>(), true));
        services.AddSingleton<IAiShotQuoteSigner, AiShotQuoteSigner>();
        services.AddSingleton<IDesignGenerationQueue, APCS.Infrastructure.BackgroundServices.DesignGenerationQueueChannel>();
        services.AddSingleton<IPlanAiReviewQueue, APCS.Infrastructure.BackgroundServices.PlanAiReviewQueueChannel>();
        services.AddSingleton<IRagIngestionQueue, APCS.Infrastructure.BackgroundServices.RagIngestionQueueChannel>();
        services.AddSingleton<IMarketResearchQueue, APCS.Infrastructure.BackgroundServices.MarketResearchQueueChannel>();
        services.AddSingleton<IAssistantConversationPurgeQueue, APCS.Infrastructure.BackgroundServices.AssistantConversationPurgeQueueChannel>();
        services.AddScoped<IBatchJobClaims, APCS.Infrastructure.BackgroundServices.BatchJobClaims>();
        services.AddOptions<GeminiEmbeddingOptions>()
            .Bind(configuration.GetSection(GeminiEmbeddingOptions.SectionName));
        services.AddOptions<RagRetrievalOptions>()
            .Bind(configuration.GetSection(RagRetrievalOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<VideoQualityOptions>()
            .Bind(configuration.GetSection(VideoQualityOptions.SectionName));
        services.AddOptions<DesignTextCheckOptions>()
            .Bind(configuration.GetSection(DesignTextCheckOptions.SectionName));
        // Wired in only when on: without the queue, design generation skips the text check and its lookups entirely.
        if (configuration.GetValue<bool>($"{DesignTextCheckOptions.SectionName}:{nameof(DesignTextCheckOptions.Enabled)}"))
        {
            services.AddSingleton<IDesignTextReader, PaddleOcrDesignTextReader>();
            services.AddSingleton<IDesignTextCheckQueue, APCS.Infrastructure.BackgroundServices.DesignTextCheckQueueChannel>();
            services.AddScoped<APCS.Application.Features.DesignGeneration.DesignTextCheckService>();
            services.AddHostedService<APCS.Infrastructure.BackgroundServices.DesignTextCheckWorker>();
        }
        services.AddOptions<GeminiTextGenerationOptions>()
            .Bind(configuration.GetSection(GeminiTextGenerationOptions.SectionName));
        services.AddOptions<APCS.Application.Features.Workflows.VideoPromptPreparationOptions>()
            .Bind(configuration.GetSection(APCS.Application.Features.Workflows.VideoPromptPreparationOptions.SectionName));
        services.AddScoped<IVideoFirstFramePreparer, MiniMaxFirstFramePreparer>();
        services.PostConfigure<GeminiTextGenerationOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                options.ApiKey = configuration[$"{GeminiEmbeddingOptions.SectionName}:ApiKey"] ?? string.Empty;
        });
        services.AddOptions<AssistantOptions>()
            .Bind(configuration.GetSection(AssistantOptions.SectionName))
            .ValidateDataAnnotations();
        services.PostConfigure<AssistantOptions>(options =>
        {
            if (!options.HasApiKeys)
                options.ApiKeys = configuration.GetOptionalStringArray($"{AssistantOptions.SectionName}:{nameof(AssistantOptions.ApiKeys)}");
            if (string.IsNullOrWhiteSpace(options.ApiKey) && !options.HasApiKeys)
                options.ApiKey = FirstConfigured(
                    configuration[$"{GeminiTextGenerationOptions.SectionName}:ApiKey"],
                    configuration[$"{GeminiEmbeddingOptions.SectionName}:ApiKey"]);
        });
        services.AddSingleton<IAssistantChatModel, GeminiAssistantChatModel>();
        services.AddOptions<EtsyOptions>()
            .Bind(configuration.GetSection(EtsyOptions.SectionName));
        services.AddOptions<PrintifyOptions>()
            .Bind(configuration.GetSection(PrintifyOptions.SectionName));
        services.AddOptions<MarketResearchOptions>()
            .Bind(configuration.GetSection(MarketResearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<TranslationOptions>()
            .Bind(configuration.GetSection(TranslationOptions.SectionName));
        services.AddHttpClient<GoogleTranslationProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("APCS-Translation/1.0");
        });
        services.AddScoped<ITranslationProvider>(provider =>
            provider.GetRequiredService<GoogleTranslationProvider>());
        services.AddHttpClient(EtsyMarketDataConnector.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<EtsyOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : $"{options.BaseUrl}/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 5, 120));
            if (!string.IsNullOrWhiteSpace(options.ApiKeyHeader))
                client.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", options.ApiKeyHeader);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("APCS-MarketResearch/1.0");
        });
        services.AddScoped<ICommerceMarketDataConnector, EtsyMarketDataConnector>();
        // A seller's own shop: the same app key, plus the seller's bearer token on each call.
        services.AddHttpClient(EtsyShopClient.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<EtsyOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : $"{options.BaseUrl}/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 5, 120));
            if (!string.IsNullOrWhiteSpace(options.ApiKeyHeader))
                client.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", options.ApiKeyHeader);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("APCS-Shop/1.0");
        });
        services.AddScoped<IEtsyShopClient, EtsyShopClient>();
        // Printify with the seller's own token; the User-Agent is required on every call.
        services.AddHttpClient(PrintifyClient.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<PrintifyOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : $"{options.BaseUrl}/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 10, 180));
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        });
        services.AddScoped<IPrintifyClient, PrintifyClient>();
        services.AddHttpClient(EtsyShopClient.MediaDownloadClientName, client => client.Timeout = TimeSpan.FromMinutes(2));
        services.AddHttpClient(EtsyShopClient.UploadClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<EtsyOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : $"{options.BaseUrl}/");
            client.Timeout = TimeSpan.FromMinutes(3);
            if (!string.IsNullOrWhiteSpace(options.ApiKeyHeader))
                client.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", options.ApiKeyHeader);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("APCS-Shop/1.0");
        });
        services.AddScoped<IMarketListingQueries, MarketListingQueries>();
        services.AddScoped<IMarketResearchScheduleService, MarketResearchScheduleService>();
        services.AddScoped<IMarketInsightScheduleService, MarketInsightScheduleService>();

        var connectionString = configuration.GetRequiredConnectionStringValue(
            ConfigurationKeys.ConnectionStrings.DefaultConnection);
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        services.AddRepositories();
        services.AddApplicationServices(configuration);
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.UnbanUsersJob>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.TrialExpiryJob>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.PlanAiReviewJob>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.SubscriptionRenewalJob>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.DesignGenerationWorker>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.DesignGenerationRecoveryService>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.RagIngestionWorker>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.MarketResearchJobWorker>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.RagIngestionRecoveryService>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.AssistantConversationPurgeWorker>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.MarketResearchSyncWorker>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.VideoQualityWorker>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.MarketInsightScheduleWorker>();
        services.AddSingleton<IWorkflowPublishQueue, APCS.Infrastructure.BackgroundServices.WorkflowPublishQueueChannel>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.WorkflowPublishWorker>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IUnitOfWork"/> and every persistence repository.
    /// </summary>
    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IAuthTokenRepository, AuthTokenRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IUsageStatisticRepository, UsageStatisticRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<ISupportTicketRepository, SupportTicketRepository>();
        services.AddScoped<IAssistantPendingActionRepository, AssistantPendingActionRepository>();
        services.AddScoped<IAssistantConversationRepository, AssistantConversationRepository>();
        services.AddScoped<IDesignTemplateRepository, DesignTemplateRepository>();
        services.AddScoped<IWorkflowStateRepository, WorkflowStateRepository>();

        return services;
    }

    /// <summary>
    /// Registers account, JWT, email, and cache service adapters.
    /// </summary>
    private static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        var keyProtection = services.AddDataProtection().SetApplicationName("APCS");
        if (configuration["ApiKeys:KeyRingPath"] is { Length: > 0 } keyRingPath)
            keyProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddScoped<IApiKeyCredentials, ApiKeyCredentials>();
        services.AddHttpClient("ApiKeyValidation", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(new[] { "Authorization" });
        // A background worker sets the acting seller on its scope; a request reads the signed-in user.
        services.AddScoped<ActingUserContext>();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddTransient<IEmailService, EmailService>();
        services.AddScoped<IPaymentGatewayClient, PayOsGatewayClient>();
        services.AddScoped<IInvoicePdfRenderer, QuestPdfInvoiceRenderer>();
        services.AddScoped<IFileStorageService, CloudinaryFileStorageService>();
        services.AddScoped<IPublicImageService, CloudinaryPublicImageService>();
        services.AddScoped<IMockupCompositor, CloudinaryMockupCompositor>();
        services.AddOptions<MockupOptions>()
            .Bind(configuration.GetSection(ConfigurationSections.Mockups));
        // Optional: without the section Free and the Pro trial use the defaults on UsageAllowanceOptions.
        services.AddOptions<APCS.Application.Features.Entitlements.UsageAllowanceOptions>()
            .Bind(configuration.GetSection(APCS.Application.Features.Entitlements.UsageAllowanceOptions.SectionName))
            .Validate(options => options.IsValid(), "UsageAllowances must not be negative.")
            .ValidateOnStart();
        // Optional: without the section the verdicts use the defaults on PlanVerdictOptions.
        services.AddOptions<PlanVerdictOptions>()
            .Bind(configuration.GetSection(PlanVerdictOptions.SectionName))
            .Validate(options => options.IsValid(), "PlanInsights thresholds are out of range.")
            .ValidateOnStart();
        // Optional too: without the section the review uses the defaults on PlanAiReviewOptions.
        services.AddOptions<APCS.Application.Features.Admin.SubscriptionPlans.AiReview.PlanAiReviewOptions>()
            .Bind(configuration.GetSection(APCS.Application.Features.Admin.SubscriptionPlans.AiReview.PlanAiReviewOptions.SectionName))
            .Validate(options => options.IsValid(), "PlanAiReview settings are out of range.")
            .ValidateOnStart();
        services.AddSingleton<IGarmentSegmenter, BiRefNetGarmentSegmenter>();
        services.AddSingleton<IClothesSegmenter, SegFormerClothesSegmenter>();
        services.AddHostedService<SegmentationModelInstaller>();
        services.AddHttpClient(SegmentationModelInstaller.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IMockupMapGenerator, ImageSharpMockupMapGenerator>();
        services.AddSingleton<IDesignBackgroundRemover, ImageSharpDesignBackgroundRemover>();
        services.AddScoped<IMockupMapService, MockupMapService>();
        services.AddHttpClient(MockupMapService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient("PrivateMedia", client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddScoped<IMediaStorage, CloudinaryMediaStorage>();
        services.AddHttpClient("VideoQualityWorker", client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<ProcessVideoQualityEvidenceRunner>();
        services.AddScoped<IVideoQualityEvidenceRunner, VideoQualityEvidenceRunner>();
        services.AddSingleton<IVideoQualityLeaseHeartbeat, VideoQualityLeaseHeartbeat>();
        services.AddHttpClient<GeminiEmbeddingProvider>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<GeminiEmbeddingOptions>>().Value;
            client.BaseAddress = new Uri(options.Endpoint);
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<GeminiTextGenerationProvider>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<GeminiTextGenerationOptions>>().Value;
            client.BaseAddress = new Uri(options.Endpoint);
            // Each of the three attempts has its own limit (AttemptTimeoutSeconds, or the request's own), so the
            // client sets none: a fixed one here would cut off a caller that allows its attempts longer.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddOptions<RagCacheOptions>()
            .Bind(configuration.GetSection(RagCacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IRagEmbeddingProvider>(provider => new MemoizedEmbeddingProvider(
            provider.GetRequiredService<GeminiEmbeddingProvider>(),
            provider.GetRequiredService<ICacheService>(),
            provider.GetRequiredService<IOptions<RagCacheOptions>>(),
            provider.GetRequiredService<ILogger<MemoizedEmbeddingProvider>>()));
        services.AddScoped<APCS.Application.Abstractions.Ai.IGeminiTextGenerationProvider>(provider => provider.GetRequiredService<GeminiTextGenerationProvider>());
        services.AddScoped<RagService>();
        services.AddScoped<IRagService>(provider => new CachedRagSearchService(
            provider.GetRequiredService<RagService>(),
            provider.GetRequiredService<ICacheService>(),
            provider.GetRequiredService<IOptions<RagCacheOptions>>(),
            provider.GetRequiredService<ILogger<CachedRagSearchService>>()));
        services.AddScoped<IListingContextRetriever, ListingContextRetriever>();
        services.AddScoped<IImageContextRetriever, ImageContextRetriever>();
        services.AddScoped<IVideoContextRetriever, VideoContextRetriever>();
        services.AddScoped<IPolicyContextRetriever, PolicyContextRetriever>();

        // ── Redis cache ──────────────────────────────────────────
        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetRequiredConfigurationSection(ConfigurationSections.Redis))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var redisOptions = configuration
            .GetSection(ConfigurationSections.Redis)
            .Get<RedisOptions>();

        if (redisOptions is null || string.IsNullOrWhiteSpace(redisOptions.ConnectionString))
        {
            throw new InvalidOperationException("Redis connection string is missing.");
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisOptions.ConnectionString;
            options.InstanceName = redisOptions.InstanceName;
        });

        services.AddScoped<ICacheService, RedisCacheService>();

        return services;
    }

    private static string FirstConfigured(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
