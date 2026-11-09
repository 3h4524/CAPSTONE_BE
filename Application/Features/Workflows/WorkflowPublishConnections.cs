using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Common;
using APCS.Common.Models;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// Checks, before a run starts, that the shops its publish steps send to are connected. Publishing comes last, after
/// the design, mock-up and video steps and both approvals, so a missing connection found only there wastes the run.
/// A shop that is only not answering right now does not stop the run: the publish step reports it and can be retried.
/// </summary>
public sealed class WorkflowPublishConnections(IShopService shops, IPrintifyService printify)
{
    public async Task<Error?> CheckAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.Nodes.Any(x => x.Type == "publish-printify"))
        {
            var connection = await printify.GetConnectionAsync(refresh: false, cancellationToken);
            if (connection.IsFailure) return connection.Error;
            var problem = connection.Value.Status switch
            {
                PrintifyStatuses.Connected or PrintifyStatuses.Unavailable => null,
                PrintifyStatuses.KeyInvalid => ShopErrors.PrintifyKeyInvalid(),
                PrintifyStatuses.NoStore => ShopErrors.PrintifyNoStore(),
                _ => ShopErrors.PrintifyNotConnected(),
            };
            if (problem is not null) return problem;
        }
        if (definition.Nodes.Any(x => x.Type == "publish-etsy"))
        {
            var shop = await shops.GetMyShopAsync(cancellationToken);
            if (shop.IsFailure) return shop.Error;
            var problem = shop.Value.Status switch
            {
                ShopConnectionStatuses.Connected or ShopConnectionStatuses.Unavailable => null,
                ShopConnectionStatuses.ReconnectRequired => EtsyReconnectRequired(),
                _ => EtsyNotConnected(),
            };
            if (problem is not null) return problem;
        }
        return null;
    }

    internal static Error EtsyNotConnected() =>
        Error.Conflict("shop.not_connected", "Connect your Etsy shop on the Shop page before running a workflow with Publish to Etsy.");

    internal static Error EtsyReconnectRequired() =>
        Error.Conflict("shop.reconnect_required", "Connect your Etsy shop again on the Shop page before running a workflow with Publish to Etsy.");
}
