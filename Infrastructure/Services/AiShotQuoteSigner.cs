using System.Security.Cryptography;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using Microsoft.AspNetCore.DataProtection;

namespace APCS.Infrastructure.Services;

public sealed class AiShotQuoteSigner(IDataProtectionProvider protection) : IAiShotQuoteSigner
{
    private readonly IDataProtector protector = protection.CreateProtector("APCS.AiShotQuote.v1");
    public string Sign(AiShotQuoteClaims claims) => protector.Protect(JsonSerializer.Serialize(claims));
    public AiShotQuoteClaims? Read(string token)
    {
        try { return JsonSerializer.Deserialize<AiShotQuoteClaims>(protector.Unprotect(token)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { return null; }
    }
}
