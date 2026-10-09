using APCS.Application.Abstractions.Email;

namespace APCS.IntegrationTests.TestSupport.Fakes;

/// <summary>
/// One message the host asked to deliver instead of reaching a real SMTP server.
/// </summary>
/// <param name="Recipient">The address the host addressed the message to.</param>
/// <param name="Subject">The subject line the host composed.</param>
/// <param name="Body">The full body the host composed, carrying any single-use link.</param>
public sealed record RecordedEmail(string Recipient, string Subject, string Body);

/// <summary>
/// Records the messages the host tried to send instead of reaching a real SMTP server.
/// </summary>
/// <remarks>
/// The body is kept because the verification and password-reset links only ever leave the process
/// inside it, and only the hash of their token is persisted. A test that drives the real register
/// or forgot-password endpoint therefore has to read the token back out of the recorded message.
/// </remarks>
public sealed class FakeEmailService : IEmailService
{
    private readonly List<RecordedEmail> _messages = [];

    /// <summary>Gets the recipients and subjects of the messages the host asked to send.</summary>
    public IReadOnlyList<(string Recipient, string Subject)> Sent =>
        _messages.Select(message => (message.Recipient, message.Subject)).ToArray();

    /// <summary>Gets every recorded message, including the body that carries a single-use link.</summary>
    public IReadOnlyList<RecordedEmail> Messages => _messages;

    /// <inheritdoc />
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _messages.Add(new RecordedEmail(to, subject, body));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendEmailVerificationAsync(
        string to,
        string fullName,
        string verificationToken,
        CancellationToken cancellationToken = default)
    {
        _messages.Add(new RecordedEmail(
            to,
            "Verify your APCS account",
            $"Hi {fullName},\n\nConfirm your APCS account by opening the link below:\n\nhttp://localhost:3000/verify-email?token={verificationToken}"));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendPasswordResetAsync(
        string to,
        string fullName,
        string resetToken,
        CancellationToken cancellationToken = default)
    {
        _messages.Add(new RecordedEmail(
            to,
            "Reset your APCS password",
            $"Hi {fullName},\n\nOpen the link below to set a new password:\n\nhttp://localhost:3000/reset-password?token={resetToken}"));
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
        _messages.Add(new RecordedEmail(
            to,
            $"New support ticket {ticketNumber}",
            $"Hi {fullName},\n\nA new support ticket needs attention.\n\nTicket: {ticketNumber}"));
        return Task.CompletedTask;
    }
}