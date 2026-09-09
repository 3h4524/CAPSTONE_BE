using APCS.Common.Models;

namespace APCS.Common.Extensions;

/// <summary>
/// Provides collection helpers that do not depend on persistence technology.
/// </summary>
public static class EnumerableExtensions
{
    /// <summary>
    /// Applies in-memory pagination to an enumerable sequence.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="source">The source sequence.</param>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>A paged result.</returns>
    public static PagedResult<T> ToPagedResult<T>(this IEnumerable<T> source, int pageNumber, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(source);

        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var items = source.ToList();
        var pageItems = items
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<T>(pageItems, items.Count, pageNumber, pageSize);
    }
}
