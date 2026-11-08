namespace APCS.Application.Features.Workflows;

/// <summary>Signals that the scene video snapshot of a workflow has changed.</summary>
public interface ISceneVideoNotifier
{
    Task ChangedAsync(Guid runId, CancellationToken cancellationToken = default);
}
