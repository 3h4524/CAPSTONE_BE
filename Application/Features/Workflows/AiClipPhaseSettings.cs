namespace APCS.Application.Features.Workflows;

/// <summary>The status and wording of a render job while the API makes its AI scenes.</summary>
public static class AiClipPhase
{
    /// <summary>
    /// The status of a render job whose AI scenes the API is still making. The media worker and the media queue only look
    /// at <c>queued</c> jobs, so they never see it until every scene is stored.
    /// </summary>
    public const string JobStatus = "generating_clips";
    public const string Stage = "Generating Assets";
    /// <summary>Progress a render shows when it starts making its scenes, as the worker did.</summary>
    public const int StartProgress = 15;
    /// <summary>Progress once every scene is stored; the worker takes it from there.</summary>
    public const int DoneProgress = 35;
}

/// <summary>
/// How the API makes the AI scenes of a render job before the media worker gets it. Bound to
/// <c>AiVideo:ClipPhase</c>. While it is off, the worker makes the scenes itself as it always did.
/// </summary>
public sealed class AiClipPhaseSettings
{
    public const string SectionName = "AiVideo:ClipPhase";

    /// <summary>New AI render jobs wait in <c>generating_clips</c> while the API makes their scenes.</summary>
    public bool Enabled { get; set; }

    /// <summary>Scenes being made at the provider at once, over every user. Keep it within what the provider account allows.</summary>
    public int MaxInFlight { get; set; } = 3;

    /// <summary>Scenes of one user being made at once, so one seller's batch does not take every slot.</summary>
    public int MaxInFlightPerUser { get; set; } = 2;

    /// <summary>Calls to the provider or to storage made at the same time in one pass.</summary>
    public int MaxParallelCalls { get; set; } = 4;

    /// <summary>How often the scenes still being made are asked about.</summary>
    public int PollIntervalSeconds { get; set; } = 10;

    /// <summary>A scene is first asked about this long after it was submitted: the provider takes minutes.</summary>
    public int FirstPollDelaySeconds { get; set; } = 30;

    /// <summary>
    /// How often a scene submitted with a callback URL is asked about. The provider calls back when it is done, so this only
    /// catches a call that never came (the provider failing to send it, the API restarting meanwhile).
    /// </summary>
    public int CallbackPollIntervalSeconds { get; set; } = 60;

    /// <summary>A scene that has no clip this long after it was submitted is given up.</summary>
    public int SceneTimeoutMinutes { get; set; } = 25;

    /// <summary>How often the provider may turn a scene away (over quota, overloaded) before the job fails.</summary>
    public int MaxBusyAttempts { get; set; } = 3;

    /// <summary>Jobs looked after in one pass.</summary>
    public int MaxJobsPerPass { get; set; } = 50;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(1, PollIntervalSeconds));
    public TimeSpan FirstPollDelay => TimeSpan.FromSeconds(Math.Max(0, FirstPollDelaySeconds));
    public TimeSpan CallbackPollInterval => TimeSpan.FromSeconds(Math.Max(1, CallbackPollIntervalSeconds));
    public TimeSpan SceneTimeout => TimeSpan.FromMinutes(Math.Max(1, SceneTimeoutMinutes));
}
