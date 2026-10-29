using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.TestReports;
using Microsoft.AspNetCore.SignalR;

namespace APCS.Api.Hubs;

/// <summary>
/// Reports the state of test runs over the SignalR hub the admin pages already listen to.
/// </summary>
public sealed class TestReportRunBroadcaster : ITestReportRunBroadcaster
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public TestReportRunBroadcaster(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PublishRunChangedAsync(string runId, string status, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group("Admins").SendAsync("TestReportRunChanged", runId, status, cancellationToken: cancellationToken);
    }
}
