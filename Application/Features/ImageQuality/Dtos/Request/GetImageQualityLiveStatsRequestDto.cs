namespace APCS.Application.Features.ImageQuality.Dtos.Request;

/// <summary>The period the live generation stats cover; an open end means no limit on that side.</summary>
public sealed class GetImageQualityLiveStatsRequestDto
{
    /// <summary>Gets or sets the first moment counted.</summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>Gets or sets the moment counting stops (not included).</summary>
    public DateTimeOffset? ToUtc { get; init; }
}
