namespace APCS.Application.Features.Commerce;

/// <summary>
/// How long a listing snapshot keeps its full text. Each day's collection stores about twenty thousand snapshots with
/// Etsy's whole listing JSON; past a week only the figures are read for history and trends, while the listing's newest
/// snapshot always keeps everything.
/// </summary>
public static class MarketSnapshotRetention
{
    /// <summary>Days a snapshot keeps its description and full raw payload.</summary>
    public const int FullTextDays = 7;

    /// <summary>Snapshots slimmed per statement, so each one holds its locks briefly.</summary>
    public const int BatchSize = 5000;

    /// <summary>Statements per collection run; the rest is slimmed on the next runs.</summary>
    public const int MaxBatchesPerRun = 10;

    /// <summary>
    /// Raw payload fields a slimmed snapshot keeps: whether it is a digital download, when it was first listed, whether
    /// it is personalizable (all read by the radar), and its Etsy category.
    /// </summary>
    public static readonly IReadOnlyList<string> KeptPayloadFields =
        ["listing_type", "original_creation_timestamp", "is_personalizable", "taxonomy_id"];
}
