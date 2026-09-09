using APCS.Application.Abstractions.Email;
using APCS.Infrastructure.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Sends application email through the configured SMTP server.
/// </summary>
/// <remarks>
/// An environment with no mail server configured logs the message instead of sending it, so
/// local development and tests need no transport.
/// </remarks>
public sealed class EmailService(
    IOptions<AppOptions> appOptions,
    IOptions<SmtpOptions> smtpOptions,
    ILogger<EmailService> logger) : IEmailService
{
    private readonly AppOptions _appOptions = appOptions.Value;
    private readonly SmtpOptions _smtpOptions = smtpOptions.Value;

    /// <inheritdoc />
    public async Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        if (!_smtpOptions.IsConfigured)
        {
            // The body can carry a single-use link, so only the recipient and subject are logged.
            logger.LogInformation(
                "Email send requested to {Recipient} with subject {Subject}",
                to,
                subject);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtpOptions.FromName, _smtpOptions.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();

        // 465 expects TLS from the first byte; 587 opens in the clear and upgrades with STARTTLS.
        var socketOptions = _smtpOptions.UsesImplicitTls
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(_smtpOptions.Host, _smtpOptions.Port, socketOptions, cancellationToken);

        // A relay that accepts anonymous submissions needs no credentials.
        if (!string.IsNullOrWhiteSpace(_smtpOptions.Username))
        {
            await client.AuthenticateAsync(_smtpOptions.Username, _smtpOptions.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation("Sent email to {Recipient} with subject {Subject}", to, subject);
    }

    /// <inheritdoc />
    public Task SendEmailVerificationAsync(
        string to,
        string fullName,
        string verificationToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationToken);

        var link = BuildVerificationLink(verificationToken);
        var body =
            $"""
             Hi {fullName},

             Confirm your APCS account by opening the link below:

             {link}

             The link expires soon. If you did not create this account, ignore this email.
             """;

        // The link carries the raw token, so only the recipient and subject are logged at the
        // default level. A developer who needs the link while no real transport is configured
        // opts in by lowering this category to Debug.
        logger.LogDebug("Verification link for {Recipient}: {VerificationLink}", to, link);

        return SendAsync(to, "Verify your APCS account", body, cancellationToken);
    }

    private string BuildVerificationLink(string verificationToken)
    {
        var baseAddress = new Uri(_appOptions.BaseUrl, UriKind.Absolute);
        var path = new Uri(baseAddress, _appOptions.VerifyEmailPath);

        return $"{path}?token={Uri.EscapeDataString(verificationToken)}";
    }
}
