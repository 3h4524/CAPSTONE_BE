using System.Text;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Constants;
using APCS.Common.Extensions;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using APCS.Infrastructure.Services;
using Microsoft.Extensions.Caching.StackExchangeRedis;
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
    /// Adds persistence, Identity, JWT, and infrastructure adapters.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetRequiredConnectionStringValue(
            ConfigurationKeys.ConnectionStrings.DefaultConnection);

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetRequiredConfigurationSection(ConfigurationSections.Jwt))
            .ValidateDataAnnotations()
            .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32, "JWT signing key must be at least 32 bytes.")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                    connectionString,
                    npgsqlOptions => npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IReadDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        services
            .AddIdentityCore<Seller>(ConfigureIdentityOptions)
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<IIdentityService, IdentityService>();
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

    private static void ConfigureIdentityOptions(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.SignIn.RequireConfirmedEmail = false;
    }
}
