using System.ComponentModel.DataAnnotations;

namespace APCS.Infrastructure.Options;

public sealed class RagRetrievalOptions
{
    public const string SectionName = "Rag:Retrieval";

    [Range(0, 1)]
    public double MinimumSimilarity { get; set; } = 0.55;

    [Range(1, 10)]
    public int MaxChunksPerDocument { get; set; } = 2;

    [Range(2, 20)]
    public int CandidateMultiplier { get; set; } = 10;

    [Range(0, 1)]
    public double LexicalWeight { get; set; } = 0.25;
}
