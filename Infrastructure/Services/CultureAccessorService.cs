using APCS.Application.Abstractions.Localization;
using Microsoft.AspNetCore.Http;

namespace APCS.Infrastructure.Services;

public sealed class CultureAccessorService(IHttpContextAccessor httpContextAccessor) : ICultureAccessor
{
    public string CurrentCulture
    {
        get
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers["Accept-Language"].ToString();
            return header is not null && header.Trim().StartsWith("vi", StringComparison.OrdinalIgnoreCase)
                ? "vi"
                : "en";
        }
    }
}
