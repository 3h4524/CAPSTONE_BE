using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>
/// Provides strongly typed outgoing mail server configuration.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = ConfigurationSections.Smtp;

    /// <summary>
    /// The port that expects TLS to be negotiated on connect rather than upgraded with STARTTLS.
    /// </summary>
    private const int ImplicitTlsPort = 465;

    /// <summary>
    /// Gets or sets the mail server host, for example <c>smtp.gmail.com</c>.
    /// </summary>
    /// <remarks>
    /// Left empty in environments with no mail server; <see cref="APCS.Infrastructure.Services.EmailService"/>
    /// then logs the message instead of sending it, rather than failing application startup.
    /// </remarks>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mail server port. 587 negotiates STARTTLS, 465 connects over TLS directly.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Gets or sets whether STARTTLS should be negotiated on non-465 ports.
    /// Disable only for a local SMTP sink such as Mailpit; keep enabled for real mail providers.
    /// </summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// Gets or sets the account used to authenticate. Leave empty for a relay that accepts
    /// anonymous submissions.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the password for <see cref="Username"/>. Gmail requires an app password
    /// rather than the account password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the address messages are sent from. Most providers require this to match
    /// <see cref="Username"/>.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown beside <see cref="FromAddress"/>.
    /// </summary>
    public string FromName { get; set; } = "APCS";

    /// <summary>
    /// Gets a value indicating whether enough is configured to actually send mail.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    /// <summary>
    /// Gets a value indicating whether the port expects TLS on connect instead of STARTTLS.
    /// </summary>
    public bool UsesImplicitTls => Port == ImplicitTlsPort;
}
