using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace APCS.Api.Filters;

public sealed class MediaWorkerAuthorizationFilter(IConfiguration configuration) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = configuration["MediaWorker:ApiKey"];
        var provided = context.HttpContext.Request.Headers["X-Media-Worker-Key"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32 || provided.Length > 256 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(provided))))
            context.Result = new UnauthorizedObjectResult(new ProblemDetails { Status = 401, Title = "Worker authentication required." });
    }
}
