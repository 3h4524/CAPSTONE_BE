namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// The Printify color name of a mock-up garment color ('#RRGGBB'), from <c>printify_color_map</c>. A row for the
/// blueprint wins over the shared one, since providers name the same shade differently.
/// </summary>
public interface IPrintifyColorMapRepository
{
    /// <summary>
    /// The Printify name of each given color for <paramref name="blueprintId"/>, keyed by the upper-case hex; colors
    /// without a row are left out. Empty, not an error, while the table does not exist yet.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> NamesAsync(long blueprintId, IReadOnlyCollection<string> hexes, CancellationToken cancellationToken);
}
