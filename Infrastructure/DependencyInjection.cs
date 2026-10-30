using System.Text;
using APCS.Application.Abstractions.AI;
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
        // lifestyle mock-ups and AI Studio frames Gemini, the only provider that can combine images.
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
        services.AddSingleton<IAiShotQuoteSigner, AiShotQuoteSigner>();
        services.AddSingleton<IDesignGenerationQueue, APCS.Infrastructure.BackgroundServices.DesignGenerationQueueChannel>();
        services.AddSingleton<IRagIngestionQueue, APCS.Infrastructure.BackgroundServices.RagIngestionQueueChannel>();
        services.AddScoped<IBatchJobClaims, APCS.Infrastructure.BackgroundServices.BatchJobClaims>();
        services.AddOptions<GeminiEmbeddingOptions>()
            .Bind(configuration.GetSection(GeminiEmbeddingOptions.SectionName));
        services.AddOptions<RagRetrievalOptions>()
            .Bind(configuration.GetSection(RagRetrievalOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<VideoQualityOptions>()
            .Bind(configuration.GetSection(VideoQualityOptions.SectionName));
        services.AddOptions<GeminiTextGenerationOptions>()
            .Bind(configuration.GetSection(GeminiTextGenerationOptions.SectionName));
        services.PostConfigure<GeminiTextGenerationOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                options.ApiKey = configuration[$"{GeminiEmbeddingOptions.SectionName}:ApiKey"] ?? string.Empty;
        });
        services.AddOptions<EtsyOptions>()
            .Bind(configuration.GetSection(EtsyOptions.SectionName));
        services.AddOptions<MarketResearchOptions>()
            .Bind(configuration.GetSection(MarketResearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
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
        services.AddScoped<IMarketResearchScheduleService, MarketResearchScheduleService>();

        var connectionString = configuration.GetRequiredConnectionStringValue(
            ConfigurationKeys.ConnectionStrings.DefaultConnection);
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        services.AddRepositories();
        services.AddApplicationServices(configuration);
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.UnbanUsersJob>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.DesignGenerationWorker>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.DesignGenerationRecoveryService>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.RagIngestionWorker>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.RagIngestionRecoveryService>();
        services.AddHostedService<APCS.Infrastructure.BackgroundServices.MarketResearchSyncWorker>();

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
        services.AddScoped<IDesignTemplateRepository, DesignTemplateRepository>();
        services.AddScoped<IWorkflowStateRepository, WorkflowStateRepository>();

        return services;
    }

    /// <summary>
    /// Registers account, JWT, email, and cache service adapters.
    /// </summary>
    private static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        var keyProtection = services.AddDataProtection().SetApplicationName("APCS");
        if (configuration["ApiKeys:KeyRingPath"] is { Length: > 0 } keyRingPath)
            keyProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddScoped<IApiKeyCredentials, ApiKeyCredentials>();
        services.AddHttpClient("ApiKeyValidation", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(new[] { "Authorization" });
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
        services.AddSingleton<IGarmentSegmenter, BiRefNetGarmentSegmenter>();
        services.AddHostedService<SegmentationModelInstaller>();
        services.AddHttpClient(SegmentationModelInstaller.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IMockupMapGenerator, ImageSharpMockupMapGenerator>();
        services.AddSingleton<IDesignBackgroundRemover, ImageSharpDesignBackgroundRemover>();
        services.AddScoped<IMockupMapService, MockupMapService>();
        services.AddHttpClient(MockupMapService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient("PrivateMedia", client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddScoped<IMediaStorage, CloudinaryMediaStorage>();
        services.AddScoped<IVideoQualityEvidenceRunner, ProcessVideoQualityEvidenceRunner>();
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
            // Each attempt has its own, shorter limit (AttemptTimeoutSeconds); this only bounds the three of them together.
            client.Timeout = TimeSpan.FromSeconds(3 * options.AttemptTimeoutSeconds + 30);
        });
        services.AddScoped<IRagEmbeddingProvider>(provider => provider.GetRequiredService<GeminiEmbeddingProvider>());
        services.AddScoped<APCS.Application.Abstractions.Ai.IGeminiTextGenerationProvider>(provider => provider.GetRequiredService<GeminiTextGenerationProvider>());
        services.AddScoped<IRagService, RagService>();
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
}
