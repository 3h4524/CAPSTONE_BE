using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using APCS.Common.Constants;
using APCS.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace APCS.Api.Extensions;

public static class ApiRateLimiting
{
    public const string AuthPolicy = "auth";
    public const string OtpPolicy = "auth-otp";
    public const string AssistantChatPolicy = "assistant-chat";

    private const int SegmentsPerWindow = 6;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(GlobalPartition);
            options.AddPolicy(AuthPolicy, context =>
                PerClientIp(context, "auth", limits => limits.AuthPermitsPerMinute));
            options.AddPolicy(OtpPolicy, context =>
                PerClientIp(context, "otp", limits => limits.OtpPermitsPerMinute));
            options.AddPolicy(AssistantChatPolicy, context =>
                SlidingWindow($"assistant:{CallerKey(context)}", OptionsOf(context).AssistantChatPermitsPerMinute));
        });
    }

    public static string CallerKey(HttpContext context) =>
        UserId(context.User) is { } userId ? $"user:{userId}" : $"ip:{ClientIp(context)}";

    private static RateLimitPartition<string> GlobalPartition(HttpContext context)
    {
        var options = OptionsOf(context);
        if (!options.Enabled)
            return RateLimitPartition.GetNoLimiter("unlimited");

        var key = CallerKey(context);
        var permits = key.StartsWith("user:", StringComparison.Ordinal)
            ? options.UserPermitsPerMinute
            : options.AnonymousPermitsPerMinute;
        return SlidingWindow(key, permits);
    }

    private static RateLimitPartition<string> PerClientIp(
        HttpContext context,
        string prefix,
        Func<RateLimitingOptions, int> permits)
    {
        var options = OptionsOf(context);
        return options.Enabled
            ? SlidingWindow($"{prefix}:{ClientIp(context)}", permits(options))
            : RateLimitPartition.GetNoLimiter("unlimited");
    }

    private static RateLimitPartition<string> SlidingWindow(string key, int permits) =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = Window,
            SegmentsPerWindow = SegmentsPerWindow,
            QueueLimit = 0
        });

    private static RateLimitingOptions OptionsOf(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static string? UserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            : null;

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? wait
            : Window / SegmentsPerWindow;
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        var response = rejected.HttpContext.Response;

        response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type = $"https://apcs/errors/{ErrorCodes.TooManyRequests}",
            Title = $"Too many requests. Try again in {seconds} seconds.",
            Status = StatusCodes.Status429TooManyRequests
        };
        problem.Extensions["code"] = ErrorCodes.TooManyRequests;
        problem.Extensions["traceId"] = rejected.HttpContext.TraceIdentifier;

        await JsonSerializer.SerializeAsync(response.Body, problem, JsonOptions, cancellationToken);
    }
}
