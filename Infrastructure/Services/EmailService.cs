using APCS.Application.Abstractions.Email;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Development email service placeholder.
/// </summary>
public sealed class EmailService(
    IOptions<AppOptions> appOptions,
    ILogger<EmailService> logger) : IEmailService
{
    private readonly AppOptions _appOptions = appOptions.Value;

    /// <inheritdoc />
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email send requested to {Recipient} with subject {Subject}", to, subject);
        return Task.CompletedTask;
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

    /// <inheritdoc />
    public Task SendPasswordResetAsync(
        string to,
        string fullName,
        string resetToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(resetToken);

        var link = BuildPasswordResetLink(resetToken);
        var body =
            $"""
             Hi {fullName},

             You requested a password reset for your APCS account. Open the link below to set a new password:

             {link}

             The link expires in 1 hour. If you did not request this, ignore this email — your password will not change.
             """;

        logger.LogDebug("Password reset link for {Recipient}: {ResetLink}", to, link);

        return SendAsync(to, "Reset your APCS password", body, cancellationToken);
    }

    private string BuildPasswordResetLink(string resetToken)
    {
        var baseAddress = new Uri(_appOptions.BaseUrl, UriKind.Absolute);
        var path = new Uri(baseAddress, _appOptions.ResetPasswordPath);

        return $"{path}?token={Uri.EscapeDataString(resetToken)}";
    }
}
