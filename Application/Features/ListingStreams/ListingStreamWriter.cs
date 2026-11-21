using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.ListingContent.Common;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Application.Features.ListingStreams.Common;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.ListingStreams;

/// <summary>
/// Writes the listing events of one request to the seller's listing stream. A failed write is logged and dropped, so a
/// Redis problem never fails the listing itself.
/// </summary>
public sealed class ListingStreamWriter(IListingStreamBuffer buffer, Guid userId, Guid streamId, ILogger logger)
{
    private int ended;

    public IListingGenerationObserver For(Guid productId) => new ProductObserver(this, productId);

    public async Task EndAsync()
    {
        if (Interlocked.Exchange(ref ended, 1) == 1)
            return;
        await AppendAsync(ListingStreamEvent.End(), CancellationToken.None);
    }

    private async Task AppendAsync(ListingStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        try
        {
            await buffer.AppendAsync(userId, streamId, streamEvent.Type, streamEvent.DataJson(), cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "Could not write the {EventType} event of listing stream {StreamId}", streamEvent.Type, streamId);
        }
    }

    private sealed class ProductObserver(ListingStreamWriter writer, Guid productId) : IListingGenerationObserver
    {
        public Task StartedAsync(string model, CancellationToken cancellationToken) =>
            writer.AppendAsync(ListingStreamEvent.Start(productId, model), cancellationToken);

        public Task TextAsync(string text, CancellationToken cancellationToken) =>
            writer.AppendAsync(ListingStreamEvent.Delta(productId, text), cancellationToken);

        public Task RestartedAsync(CancellationToken cancellationToken) =>
            writer.AppendAsync(ListingStreamEvent.Reset(productId), cancellationToken);

        public Task CompletedAsync(ListingContentResponseDto listing, CancellationToken cancellationToken) =>
            writer.AppendAsync(ListingStreamEvent.Done(productId, listing), cancellationToken);

        public Task FailedAsync(string message, CancellationToken cancellationToken) =>
            writer.AppendAsync(ListingStreamEvent.Failed(productId, message), cancellationToken);
    }
}
