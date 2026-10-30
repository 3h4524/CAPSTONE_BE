using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>System-owned MiniMax configuration shared by every AI-video request.</summary>
public sealed class MiniMaxOptions
{
    public const string SectionName = ConfigurationSections.MiniMax;

    /// <summary>The platform MiniMax API key. Configure it with <c>MiniMax__ApiKey</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
