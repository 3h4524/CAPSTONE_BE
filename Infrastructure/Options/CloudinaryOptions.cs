using System.ComponentModel.DataAnnotations;
using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>Configures Cloudinary private storage for support-ticket attachments.</summary>
public sealed class CloudinaryOptions
{
    public const string SectionName = ConfigurationSections.Cloudinary;

    [Required]
    public string CloudName { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string ApiSecret { get; set; } = string.Empty;

    public string Folder { get; set; } = "apcs";

    [Range(1, 60)]
    public int SignedUrlTtlMinutes { get; set; } = 10;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);
}
