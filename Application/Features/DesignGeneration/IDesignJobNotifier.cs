namespace APCS.Application.Features.DesignGeneration;

/// <summary>
/// Tells a Seller's open pages that a design job changed, so they read it again at once. Polling stays as the fallback
/// for a page that is not connected, or a job run by another instance of the API. It never throws.
/// </summary>
public interface IDesignJobNotifier
{
    Task ChangedAsync(Guid userId, Guid batchJobId, CancellationToken cancellationToken = default);
}
