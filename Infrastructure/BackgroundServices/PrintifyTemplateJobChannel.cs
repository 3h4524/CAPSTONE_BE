using System.Threading.Channels;
using APCS.Application.Features.BatchMockups.Printify;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>In-process queue of Printify measuring jobs. A job lost to a restart stays queued until it expires; the admin starts it again.</summary>
public sealed class PrintifyTemplateJobChannel : IPrintifyTemplateJobQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid jobId) => channel.Writer.TryWrite(jobId);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) => channel.Reader.ReadAsync(cancellationToken);
}
