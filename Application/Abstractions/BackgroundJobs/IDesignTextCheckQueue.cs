namespace APCS.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Hands saved designs to the background OCR check of their text, so reading them (seconds each on a CPU) never slows
/// down the job that makes them. In-process: designs waiting when the app stops are simply not checked.
/// </summary>
public interface IDesignTextCheckQueue
{
    /// <summary>Queues a design; false when the queue is full, and that design is not checked.</summary>
    bool TryEnqueue(DesignTextCheckRequest request);

    /// <summary>Waits for the next design to check.</summary>
    ValueTask<DesignTextCheckRequest> DequeueAsync(CancellationToken cancellationToken);
}

/// <summary>A saved design to check: its image as uploaded, and the phrase it should print (null for no words).</summary>
public sealed record DesignTextCheckRequest(Guid DesignImageId, byte[] Image, string? Phrase);
