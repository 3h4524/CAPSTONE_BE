namespace APCS.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Wakes the background worker that translates system art styles into Vietnamese. The pending state lives on the
/// row itself, so the request returns at once and readers see English until the translation is saved.
/// </summary>
public interface IStyleArtPresetTranslationQueue
{
    /// <summary>Tells the worker that a system style is waiting for its translation.</summary>
    void Notify();
}
