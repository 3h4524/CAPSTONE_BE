using System.Text;
using APCS.Api.Extensions;
using APCS.Api.Middleware;
using APCS.Application;
using APCS.Application.Abstractions.Caching;
using APCS.Common.Constants;
using APCS.Common.Extensions;
using APCS.Infrastructure;
using APCS.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

DotEnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);
var jwtOptions = builder.Configuration.GetRequiredOptions<JwtOptions>(ConfigurationSections.Jwt);
var allowedOrigins = builder.Configuration.GetOptionalStringArray(ConfigurationKeys.Cors.AllowedOrigins);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "APCS API",
        Version = "v1"
    });

    var bearerScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter: Bearer {access token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = JwtBearerDefaults.AuthenticationScheme
        }
    };

    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, bearerScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [bearerScheme] = Array.Empty<string>()
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            policy.AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // The access token never reaches client-side JS: it travels only in the HttpOnly
        // __Host-apcs_access cookie, so it is read from there instead of an Authorization
        // header. Falling through when the cookie is absent keeps the Authorization header
        // path available for non-browser callers (Swagger, future mobile clients).
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(
                        AuthConstants.AccessTokenCookieName, out var accessToken)
                    && !string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapGet("/api/dev/redis-test", async (
        ICacheService cache,
        CancellationToken cancellationToken) =>
    {
        const string key = "dev:redis-test";

        var payload = new
        {
            Message = "Redis works",
            Time = DateTime.UtcNow
        };

        await cache.SetAsync(key, payload, TimeSpan.FromMinutes(1), cancellationToken);

        var cached = await cache.GetAsync<object>(key, cancellationToken);

        return Results.Ok(cached);
    });

    // One-time (or on-ngrok-restart) dev setup: registers the webhook URL PayOS should call.
    // Example: curl -X POST "http://localhost:5191/api/dev/confirm-payos-webhook?url=https://<id>.ngrok-free.app/api/subscriptions/webhooks/payos"
    app.MapPost("/api/dev/confirm-payos-webhook", async (
        string url,
        APCS.Application.Features.Subscriptions.Common.IPaymentGatewayClient paymentGateway,
        CancellationToken cancellationToken) =>
    {
        await paymentGateway.ConfirmWebhookAsync(url, cancellationToken);
        return Results.Ok(new { confirmed = url });
    });
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
