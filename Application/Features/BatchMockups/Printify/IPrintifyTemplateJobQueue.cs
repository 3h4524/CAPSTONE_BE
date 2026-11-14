namespace APCS.Application.Features.BatchMockups.Printify;

/// <summary>Measuring jobs waiting to run in the background, one at a time.</summary>
public interface IPrintifyTemplateJobQueue
{
    void Enqueue(Guid jobId);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
