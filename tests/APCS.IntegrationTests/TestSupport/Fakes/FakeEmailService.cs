using APCS.Application.Abstractions.Email;

namespace APCS.IntegrationTests.TestSupport.Fakes;

/// <summary>
/// Records the messages the host tried to send instead of reaching a real SMTP server.
/// </summary>
public sealed class FakeEmailService : IEmailService
{
    private readonly List<(string Recipient, string Subject)> _sent = [];

    /// <summary>Gets the messages the host asked to send.</summary>
    public IReadOnlyList<(string Recipient, string Subject)> Sent => _sent;

    /// <inheritdoc />
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _sent.Add((to, subject));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendEmailVerificationAsync(
        string to,
        string fullName,
        string verificationToken,
        CancellationToken cancellationToken = default)
    {
        _sent.Add((to, "Verify your APCS account"));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendPasswordResetAsync(
        string to,
        string fullName,
        string resetToken,
        CancellationToken cancellationToken = default)
    {
        _sent.Add((to, "Reset your APCS password"));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendSupportTicketCreatedAsync(
        string to,
        string fullName,
        Guid ticketId,
        string ticketNumber,
        string category,
        string priority,
        CancellationToken cancellationToken = default)
    {
        _sent.Add((to, $"New support ticket {ticketNumber}"));
        return Task.CompletedTask;
    }
}
