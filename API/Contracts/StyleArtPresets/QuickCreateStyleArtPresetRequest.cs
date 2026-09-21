namespace APCS.Api.Contracts.StyleArtPresets;

/// <summary>JSON body used to quickly create a seller art style with only a name.</summary>
public sealed class QuickCreateStyleArtPresetRequest
{
    public string Name { get; set; } = string.Empty;
}
