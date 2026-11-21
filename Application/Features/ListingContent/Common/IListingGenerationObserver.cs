using APCS.Application.Features.ListingContent.Dtos;

namespace APCS.Application.Features.ListingContent.Common;

public interface IListingGenerationObserver
{
    Task StartedAsync(string model, CancellationToken cancellationToken);

    Task TextAsync(string text, CancellationToken cancellationToken);

    Task RestartedAsync(CancellationToken cancellationToken);

    Task CompletedAsync(ListingContentResponseDto listing, CancellationToken cancellationToken);

    Task FailedAsync(string message, CancellationToken cancellationToken);
}
