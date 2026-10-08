using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Common.Constants;
using APCS.Infrastructure.BackgroundServices;
using APCS.IntegrationTests.TestSupport.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace APCS.IntegrationTests.TestSupport;

/// <summary>
/// Hosts the real API pipeline in memory so a test can drive the production middleware, routing,
/// authentication, and authorization stack over HTTP.
/// </summary>
/// <remarks>
/// Three independent layers keep the suite away from real third parties, so no single mistake is
/// enough to send mail, upload an asset, or start a payment:
/// <list type="number">
/// <item><description>
/// <see cref="TestEnvironment"/> publishes an empty value for every third-party credential before
/// the host boots, so <c>DotEnvLoader</c> never copies the developer <c>.env</c> secrets in.
/// </description></item>
/// <item><description>
/// The configuration override blanks the same keys again at the host level, which also covers any
/// value that reached <c>appsettings.json</c>.
/// </description></item>
/// <item><description>
/// <see cref="ConfigureTestServices"/> replaces each adapter with an in-memory fake and swaps the
/// outbound <see cref="HttpClient"/> handler for one that throws.
/// </description></item>
/// </list>
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private const string BlockedOutboundClientName = "ApiKeyValidation";

    private readonly PostgresFixture _database;
    private readonly FakeEmailService _email = new();
    private readonly FakePaymentGatewayClient _paymentGateway = new();
    private readonly FakeFileStorageService _fileStorage = new();
    private readonly FakePublicImageService _publicImages = new();
    private readonly FakeGoogleAuthService _googleAuth = new();

    /// <summary>Initializes a new instance of the <see cref="ApiFactory"/> class.</summary>
    /// <param name="database">The fixture supplying the PostgreSQL and Redis endpoints.</param>
    /// <exception cref="InvalidOperationException">Thrown when the fixture has no usable endpoint.</exception>
    public ApiFactory(PostgresFixture database)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (!database.IsAvailable || database.PostgresConnectionString is null || database.RedisConnectionString is null)
        {
            throw new InvalidOperationException(
                "The API factory cannot be created because the database fixture has no usable endpoint. "
                + "Call PostgresFixture.AcquireAsync() and check IsAvailable first.");
        }

        TestEnvironment.Apply();
        TestEnvironment.Set("ConnectionStrings:DefaultConnection", database.PostgresConnectionString);
        TestEnvironment.Set("Redis:ConnectionString", database.RedisConnectionString);

        _database = database;
    }

    /// <summary>
    /// Creates a client that follows redirects and keeps cookies, which is how a browser client
    /// reaches the API: the access token travels in an HttpOnly cookie rather than a header.
    /// </summary>
    /// <param name="accessToken">An optional bearer token for a non-browser caller.</param>
    /// <returns>A client bound to the hosted application.</returns>
    public HttpClient CreateAuthenticatedClient(string? accessToken = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(TestEnvironment.EnvironmentName);

        builder.ConfigureAppConfiguration(configuration =>
        {
            var overrides = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{ConfigurationSections.ConnectionStrings}:{ConfigurationKeys.ConnectionStrings.DefaultConnection}"] =
                    _database.PostgresConnectionString,
                [$"{ConfigurationSections.Redis}:ConnectionString"] = _database.RedisConnectionString,
                [$"{ConfigurationSections.Redis}:InstanceName"] = "APCS:test:",
                [$"{ConfigurationSections.GoogleAuth}:ClientId"] = string.Empty,
                [$"{ConfigurationSections.Smtp}:Host"] = string.Empty,
                [$"{ConfigurationSections.Smtp}:Username"] = string.Empty,
                [$"{ConfigurationSections.Smtp}:Password"] = string.Empty,
                [$"{ConfigurationSections.Smtp}:FromAddress"] = string.Empty,
                [$"{ConfigurationSections.Cloudinary}:CloudName"] = string.Empty,
                [$"{ConfigurationSections.Cloudinary}:ApiKey"] = string.Empty,
                [$"{ConfigurationSections.Cloudinary}:ApiSecret"] = string.Empty,
                [$"{ConfigurationSections.PayOs}:ClientId"] = string.Empty,
                [$"{ConfigurationSections.PayOs}:ApiKey"] = string.Empty,
                [$"{ConfigurationSections.PayOs}:ChecksumKey"] = string.Empty
            };

            configuration.AddInMemoryCollection(overrides);
        });

        builder.ConfigureTestServices(NeutralizeThirdParties);
    }

    /// <summary>
    /// Removes the background job that unban-expires suspended accounts and swaps every
    /// third-party adapter for an in-memory fake.
    /// </summary>
    /// <remarks>
    /// <c>UnbanUsersJob</c> writes to <c>users</c> and <c>audit_logs</c> immediately on boot and
    /// then hourly. Leaving it enabled would race a test that is asserting on those tables, so its
    /// own descriptor is removed rather than every <see cref="IHostedService"/>, which would also
    /// remove the host's own server service.
    /// </remarks>
    /// <param name="services">The service collection under test.</param>
    private void NeutralizeThirdParties(IServiceCollection services)
    {
        RemoveUnbanUsersJob(services);

        services.RemoveAll<IEmailService>();
        services.AddSingleton<IEmailService>(_email);

        services.RemoveAll<IPaymentGatewayClient>();
        services.AddSingleton<IPaymentGatewayClient>(_paymentGateway);

        services.RemoveAll<IFileStorageService>();
        services.AddSingleton<IFileStorageService>(_fileStorage);

        services.RemoveAll<IPublicImageService>();
        services.AddSingleton<IPublicImageService>(_publicImages);

        services.RemoveAll<IGoogleAuthService>();
        services.AddSingleton<IGoogleAuthService>(_googleAuth);

        services.AddHttpClient(BlockedOutboundClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new BlockedOutboundHandler(BlockedOutboundClientName));
    }

    private static void RemoveUnbanUsersJob(IServiceCollection services)
    {
        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(UnbanUsersJob))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}
