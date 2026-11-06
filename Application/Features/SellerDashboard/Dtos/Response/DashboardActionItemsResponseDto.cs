namespace APCS.Application.Features.SellerDashboard.Dtos.Response;

/// <summary>What waits on the Seller, the most severe first and by priority within one severity.</summary>
/// <param name="Items">One item per kind of work, never one per product.</param>
public sealed record DashboardActionItemsResponseDto(IReadOnlyList<ActionItemDto> Items);

/// <summary>A kind of work that waits on the Seller, grouped across products.</summary>
/// <param name="Type">The kind of work, see <see cref="Common.ActionItemTypes"/>.</param>
/// <param name="Severity">"info", "warning", "error" or "critical".</param>
/// <param name="Count">The products or records it holds back.</param>
/// <param name="Priority">The sort key, see <see cref="Common.ActionItemPriority"/>.</param>
/// <param name="OldestAt">When the oldest of them started waiting.</param>
/// <param name="ActionUrl">
/// Where the Seller deals with it: the one place when all of it is in one batch job, workflow or batch,
/// otherwise the page that lists them.
/// </param>
/// <param name="Targets">Where the work sits, the largest share first, at most three.</param>
public sealed record ActionItemDto(
    string Type,
    string Severity,
    int Count,
    int Priority,
    DateTime? OldestAt,
    string ActionUrl,
    IReadOnlyList<ActionTargetDto> Targets);

/// <summary>One place an action item's work sits, e.g. a batch job with 12 designs to review.</summary>
/// <param name="Label">The batch job, workflow or batch it is in.</param>
/// <param name="Count">How much of the item's work is there.</param>
/// <param name="Url">The exact page, see <see cref="Common.DashboardLinks"/>.</param>
public sealed record ActionTargetDto(string Label, int Count, string Url);
