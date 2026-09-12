using System.Text;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        var connectionString = configuration.GetRequiredConnectionStringValue(
            ConfigurationKeys.ConnectionStrings.DefaultConnection);
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddRepositories();
        services.AddApplicationServices(configuration);

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

        return services;
    }

    /// <summary>
    /// Registers account, JWT, email, and cache service adapters.
    /// </summary>
    private static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddTransient<IEmailService, EmailService>();
        services.AddScoped<IPaymentGatewayClient, PayOsGatewayClient>();

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
