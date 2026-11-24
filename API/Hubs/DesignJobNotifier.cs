using APCS.Application.Features.DesignGeneration;
using Microsoft.AspNetCore.SignalR;

namespace APCS.Api.Hubs;

public sealed class DesignJobNotifier(IHubContext<NotificationHub> hub, ILogger<DesignJobNotifier> logger) : IDesignJobNotifier
{
    public async Task ChangedAsync(Guid userId, Guid batchJobId, CancellationToken cancellationToken = default)
    {
        try
        {
            await hub.Clients.User(userId.ToString()).SendAsync("DesignJobChanged", batchJobId.ToString("D"), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The image is already saved. A page that missed this reads it at its next poll.
            logger.LogWarning(ex, "Could not notify the seller of a change to design job {BatchJobId}", batchJobId);
        }
    }
}
