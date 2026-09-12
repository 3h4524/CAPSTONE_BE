namespace APCS.Application.Abstractions.Email;

/// <summary>
/// Sends application email messages.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email message.
    /// </summary>
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the account verification email carrying a single-use activation link.
    /// </summary>
    /// <param name="to">The recipient address.</param>
    /// <param name="fullName">The recipient's display name.</param>
    /// <param name="verificationToken">The raw verification token to embed in the link.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// The subject, body, and link address are composed by the implementation so the client
    /// address stays in configuration instead of leaking into use cases.
    /// </remarks>
    Task SendEmailVerificationAsync(
        string to,
        string fullName,
        string verificationToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the password reset email carrying a single-use reset link.
    /// </summary>
    /// <param name="to">The recipient address.</param>
    /// <param name="fullName">The recipient's display name.</param>
    /// <param name="resetToken">The raw reset token to embed in the link.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// The subject, body, and link address are composed by the implementation so the client
    /// address stays in configuration instead of leaking into use cases.
    /// </remarks>
    Task SendPasswordResetAsync(
        string to,
        string fullName,
        string resetToken,
        CancellationToken cancellationToken = default);

    /// <summary>Sends an administrator a notification about a newly created support ticket.</summary>
    Task SendSupportTicketCreatedAsync(
        string to,
        string fullName,
        Guid ticketId,
        string ticketNumber,
        string category,
        string priority,
        CancellationToken cancellationToken = default);
}
