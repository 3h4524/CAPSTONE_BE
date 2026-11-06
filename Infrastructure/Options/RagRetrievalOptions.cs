using System.ComponentModel.DataAnnotations;

namespace APCS.Infrastructure.Options;

public sealed class RagRetrievalOptions
{
    public const string SectionName = "Rag:Retrieval";

    [Range(0, 1)]
    public double MinimumSimilarity { get; set; } = 0.55;

    /// <summary>
    /// Share of the query terms a chunk must contain to be kept on lexical overlap alone, when its
    /// vector similarity is below <see cref="MinimumSimilarity"/>.
    /// </summary>
    public double MinimumLexicalScore { get; set; } = 0.5;

    /// <summary>
    /// Lets pgvector 0.8+ keep scanning the HNSW index until the filters are satisfied
    /// (hnsw.iterative_scan = relaxed_order). Turn off on older pgvector versions.
    /// </summary>
    public bool UseIterativeIndexScan { get; set; } = true;

    [Range(1, 10)]
    public int MaxChunksPerDocument { get; set; } = 2;

    [Range(2, 20)]
    public int CandidateMultiplier { get; set; } = 10;

    [Range(0, 1)]
    public double LexicalWeight { get; set; } = 0.25;

    [Range(0, 1)]
    public double ViralWeight { get; set; } = 0.15;

    [Range(0, 1)]
    public double FreshnessWeight { get; set; } = 0.15;

    [Range(1, 3650)]
    public int FreshnessHalfLifeDays { get; set; } = 30;
}
