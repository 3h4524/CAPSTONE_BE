namespace APCS.Application.Abstractions.Rag;

public interface IRagEmbeddingProvider
{
    string ModelName { get; }
    int Dimensions { get; }
    Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
