using System.ComponentModel.DataAnnotations;

namespace APCS.Application.Features.Assistant.Common;

public sealed class AssistantTurnOptions
{
    public const string SectionName = "Assistant";

    private static readonly TimeSpan StaleGrace = TimeSpan.FromMinutes(1);

    [Range(10, 1800)]
    public int TurnTimeoutSeconds { get; set; } = 180;

    public TimeSpan TurnTimeout => TimeSpan.FromSeconds(TurnTimeoutSeconds);

    public TimeSpan StaleAfter => TurnTimeout + StaleGrace;
}
