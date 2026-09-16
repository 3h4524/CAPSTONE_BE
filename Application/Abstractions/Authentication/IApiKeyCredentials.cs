namespace APCS.Application.Abstractions.Authentication;

public interface IApiKeyCredentials
{
    string Protect(string value);
    string Unprotect(string value);
    Task<bool> ValidateAsync(string provider, string value, CancellationToken cancellationToken);
}
