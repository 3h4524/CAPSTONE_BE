namespace APCS.Application.Abstractions.BackgroundJobs;

public interface IRagIngestionQueue
{
    void Enqueue(Guid documentId);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
