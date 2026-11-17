using System.Security.Cryptography;
using System.Xml.Linq;
using APCS.Application.Abstractions.AI;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Encrypts the platform's keys with a DataProtection key ring of their own, kept in the database
/// (<see cref="SystemApiKeyRingRepository"/>) rather than in a folder on one machine, so every server on the same database
/// reads a key any of them saved. The Seller keys keep the application's own ring and are not touched.
/// </summary>
/// <remarks>
/// DataProtection reads its ring once and keeps it for a day. A key saved through another server that has just made a ring
/// key of its own (two servers starting on an empty ring at once, or a new ring key every 90 days) is then unknown here:
/// the ring is read again, at most every <see cref="ReloadInterval"/>, and the key tried once more.
/// </remarks>
public sealed class SystemApiKeySecrets : ISystemApiKeySecrets, IDisposable
{
    /// <summary>What the keys are encrypted for; another purpose cannot read them.</summary>
    public const string Purpose = "APCS.SystemApiKeys.v1";

    private const string ApplicationName = "APCS.SystemApiKeys";

    /// <summary>The least time between two reads of the ring for a key it did not know.</summary>
    public static readonly TimeSpan ReloadInterval = TimeSpan.FromSeconds(30);

    private readonly IXmlRepository ring;
    private readonly ILoggerFactory? loggerFactory;
    private readonly TimeProvider time;
    private readonly object gate = new();
    private Protection current;
    private DateTimeOffset loadedAt;

    public SystemApiKeySecrets(IXmlRepository ring, TimeProvider time, ILoggerFactory? loggerFactory = null)
    {
        this.ring = ring;
        this.time = time;
        this.loggerFactory = loggerFactory;
        current = Build();
        // The ring is first read on the first use, so a key unknown then is worth one more read straight away.
        loadedAt = DateTimeOffset.MinValue;
    }

    public string Protect(string value) => current.Protector.Protect(value);

    public string Unprotect(string value)
    {
        var protection = current;
        try
        {
            return protection.Protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            // Read the ring again, unless it was read a moment ago, and try once more.
            return Reload(protection)?.Protector.Unprotect(value) ?? throw new CryptographicException(
                "The system API key was encrypted by a ring key this server cannot find.");
        }
    }

    public void Dispose() => current.Services.Dispose();

    // A fresh ring, unless another call already read it again since `seen` was in use or it was read too recently.
    private Protection? Reload(Protection seen)
    {
        lock (gate)
        {
            if (!ReferenceEquals(current, seen)) return current;
            if (time.GetUtcNow() - loadedAt < ReloadInterval) return null;
            // The old container is left for the collector: a call still holding its protector keeps working.
            current = Build();
            loadedAt = time.GetUtcNow();
            return current;
        }
    }

    // A DataProtection container of its own, so neither the ring nor the application name is shared with Seller keys.
    // Setting the repository leaves the ring unencrypted at rest (no Windows DPAPI): every server, on any OS, can read it.
    private Protection Build()
    {
        var services = new ServiceCollection();
        if (loggerFactory is not null) services.AddSingleton(loggerFactory);
        services.AddLogging();
        services.AddDataProtection().SetApplicationName(ApplicationName);
        services.Configure<KeyManagementOptions>(options => options.XmlRepository = ring);
        var provider = services.BuildServiceProvider();
        return new Protection(provider, provider.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose));
    }

    private sealed record Protection(ServiceProvider Services, IDataProtector Protector);
}

/// <summary>The ring of <see cref="SystemApiKeySecrets"/>, one row of <c>system_api_key_ring</c> per ring key.</summary>
public sealed class SystemApiKeyRingRepository(IServiceScopeFactory scopes) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.SystemApiKeyRings.AsNoTracking()
            .OrderBy(row => row.CreatedAt)
            .Select(row => row.Xml)
            .ToList()
            .Select(xml => XElement.Parse(xml))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SystemApiKeyRings.Add(new SystemApiKeyRing
        {
            Id = Guid.NewGuid(),
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
            CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }
}
