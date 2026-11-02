using APCS.Application.Features.Entitlements;

namespace APCS.Application.UnitTests.TestSupport;

/// <summary>
/// The plan feature catalog for tests: the definitions in code, or the given entries.
/// </summary>
internal sealed class TestFeatureCatalog(IReadOnlyList<FeatureCatalogEntry>? entries = null) : IFeatureCatalog
{
    private readonly IReadOnlyList<FeatureCatalogEntry> entries = entries ?? FeatureCatalogEntry.Defaults;

    public int Invalidations { get; private set; }

    public Task<IReadOnlyList<FeatureCatalogEntry>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(entries);

    public Task<FeatureCatalogEntry?> FindAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(entries.FirstOrDefault(entry => entry.Code == code));

    public void Invalidate() => Invalidations++;
}
