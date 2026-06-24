namespace APCS.Application.Common.Interfaces;

/// <summary>
/// Sends application email messages.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email message.
    /// </summary>
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
