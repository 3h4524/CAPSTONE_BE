using System.Net.Http.Headers;
using APCS.Application.Abstractions.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace APCS.Infrastructure.Services;

public sealed class ApiKeyCredentials(IDataProtectionProvider protection, IHttpClientFactory clients) : IApiKeyCredentials
{
    private readonly IDataProtector protector = protection.CreateProtector("APCS.ApiKeys.v1");
    public string Protect(string value) => protector.Protect(value);
    public string Unprotect(string value) => protector.Unprotect(value);

    public async Task<bool> ValidateAsync(string provider, string value, CancellationToken cancellationToken)
    {
        var url = provider switch
        {
            "openai" => "https://api.openai.com/v1/models",
            "replicate" => "https://api.replicate.com/v1/account",
            "printify" => "https://api.printify.com/v1/shops.json",
            _ => null
        };
        if (url is null) return false;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        request.Headers.UserAgent.ParseAdd("APCS/1.0");
        try
        {
            using var response = await clients.CreateClient("ApiKeyValidation")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
}
