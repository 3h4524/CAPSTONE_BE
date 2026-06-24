using APCS.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Development email service placeholder.
/// </summary>
public sealed class EmailService(ILogger<EmailService> logger) : IEmailService
{
    /// <inheritdoc />
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email send requested to {Recipient} with subject {Subject}", to, subject);
        return Task.CompletedTask;
    }
}
