using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>
/// Provides strongly typed PayOS payment gateway configuration.
/// </summary>
public sealed class PayOsOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = ConfigurationSections.PayOs;

    /// <summary>
    /// Gets or sets the PayOS payment channel's Client ID.
    /// </summary>
    /// <remarks>
    /// Left empty in environments that have not created a PayOS payment channel yet;
    /// <see cref="APCS.Infrastructure.Services.PayOsGatewayClient"/> is only registered when this
    /// is set (see <c>Infrastructure/DependencyInjection.cs</c>), so a dev box without PayOS keys
    /// still boots.
    /// </remarks>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the PayOS payment channel's API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the PayOS payment channel's checksum key, used to sign requests and verify webhooks.
    /// </summary>
    public string ChecksumKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the USD-to-VND conversion rate used to translate a plan's USD price into the
    /// VND amount PayOS requires.
    /// </summary>
    public decimal UsdToVndRate { get; set; } = 25000m;

    /// <summary>
    /// Gets or sets a fixed VND amount that, when set, is charged for every checkout instead of
    /// the real converted plan price.
    /// </summary>
    /// <remarks>
    /// PayOS has no sandbox: every checkout is a real bank transfer. This is a deliberate safety
    /// valve for local testing so iterating on the integration never risks the real plan price —
    /// leave unset in any environment where real prices should actually be charged.
    /// </remarks>
    public int? TestAmountVnd { get; set; }
}
