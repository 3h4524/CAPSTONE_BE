namespace APCS.Application.Abstractions.BackgroundJobs;

public interface IAssistantConversationPurgeQueue
{
    void RequestPurge();

    ValueTask WaitForRequestAsync(CancellationToken cancellationToken);
}
