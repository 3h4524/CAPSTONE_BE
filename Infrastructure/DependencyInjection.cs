using System.Text;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
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

        services.AddOptions<AppOptions>()
            .Bind(configuration.GetRequiredConfigurationSection(ConfigurationSections.App))
            .ValidateDataAnnotations()
            .ValidateOnStart();

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
        services.AddTransient<IEmailService, EmailService>();

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
