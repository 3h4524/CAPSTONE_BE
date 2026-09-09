namespace APCS.Common.Models;

/// <summary>
/// Represents a paged query result.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The current page items.</param>
/// <param name="TotalCount">The total item count.</param>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="PageSize">The page size.</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
