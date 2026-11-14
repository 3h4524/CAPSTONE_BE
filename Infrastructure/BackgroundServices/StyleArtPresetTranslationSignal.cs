using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// The in-process <see cref="IStyleArtPresetTranslationQueue"/>. A save on another API instance is only seen when
/// this worker next reads the styles, so the worker's idle sleep is bounded.
/// </summary>
public sealed class StyleArtPresetTranslationSignal : IStyleArtPresetTranslationQueue
{
    public ActivitySignal Saved { get; } = new();

    public void Notify() => Saved.Notify();
}
