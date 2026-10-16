using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Application.Features.Rag.Common;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace APCS.Infrastructure.Services;

public sealed class RagService(
    AppDbContext dbContext,
    IRagEmbeddingProvider embeddingProvider)
    : IRagService
{
    private const int ChunkSize = 1400;
    private const int ChunkOverlap = 200;

    public async Task<Guid> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Document content cannot be empty.", nameof(request));

        var normalizedContent = Normalize(request.Content);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedContent)));
        var existing = await dbContext.RagDocuments
            .AsNoTracking()
            .Where(x => x.SourceType == request.SourceType && x.ContentHash == hash)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (existing != Guid.Empty)
            return existing;

        var document = new RagDocument
        {
            Title = request.Title.Trim(),
            SourceType = request.SourceType.Trim(),
            ContentType = NormalizeContentType(request.ContentType),
            SourceUrl = request.SourceUrl?.Trim(),
            Version = request.Version?.Trim(),
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim(),
            ContentHash = hash
        };

        var metadata = JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, object?>());
        var chunks = SplitIntoChunks(normalizedContent).ToArray();
        if (chunks.Length == 0)
            throw new ArgumentException("Document content cannot be empty after normalization.", nameof(request));

        foreach (var (content, index) in chunks.Select((content, index) => (content, index)))
        {
            var vector = await embeddingProvider.CreateEmbeddingAsync(content, cancellationToken);
            var chunk = new RagChunk
            {
                DocumentId = document.Id,
                Content = content,
                ChunkIndex = index,
                TokenCount = EstimateTokenCount(content),
                Metadata = metadata
            };
            chunk.Embeddings.Add(new RagEmbedding
            {
                ChunkId = chunk.Id,
                Embedding = new Vector(vector),
                EmbeddingModel = embeddingProvider.ModelName
            });
            document.Chunks.Add(chunk);
        }

        dbContext.RagDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        return document.Id;
    }

    public async Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(
        RagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Search query cannot be empty.", nameof(request));

        var topK = Math.Clamp(request.TopK, 1, 50);
        var queryVector = new Vector(await embeddingProvider.CreateEmbeddingAsync(request.Query, cancellationToken));

        var query = dbContext.RagEmbeddings
            .AsNoTracking()
            .Where(x => x.EmbeddingModel == embeddingProvider.ModelName);

        if (!string.IsNullOrWhiteSpace(request.SourceType))
            query = query.Where(x => x.Chunk.Document.SourceType == request.SourceType);
        if (!string.IsNullOrWhiteSpace(request.Language))
            query = query.Where(x => x.Chunk.Document.Language == request.Language);
        if (!string.IsNullOrWhiteSpace(request.ContentType))
            query = query.Where(x => x.Chunk.Document.ContentType == NormalizeContentType(request.ContentType));

        var rows = await query
            .OrderBy(x => x.Embedding.CosineDistance(queryVector))
            .Take(topK)
            .Select(x => new
            {
                x.ChunkId,
                DocumentId = x.Chunk.DocumentId,
                Title = x.Chunk.Document.Title,
                ContentType = x.Chunk.Document.ContentType,
                x.Chunk.Content,
                x.Chunk.Document.SourceUrl,
                Distance = x.Embedding.CosineDistance(queryVector),
                x.Chunk.Metadata
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new RagSearchResultDto(
            row.ChunkId,
            row.DocumentId,
            row.Title,
            row.ContentType,
            row.Content,
            row.SourceUrl,
            Math.Max(0, 1 - row.Distance),
            DeserializeMetadata(row.Metadata))).ToArray();
    }

    private static IEnumerable<string> SplitIntoChunks(string content)
    {
        var start = 0;
        while (start < content.Length)
        {
            var length = Math.Min(ChunkSize, content.Length - start);
            var end = start + length;
            if (end < content.Length)
            {
                var boundary = content.LastIndexOfAny([' ', '\n', '.', ',', ';', ':'], end - 1, Math.Min(250, length));
                if (boundary > start + ChunkSize / 2)
                    end = boundary + 1;
            }

            yield return content[start..end].Trim();
            if (end >= content.Length) yield break;
            start = Math.Max(start + 1, end - ChunkOverlap);
        }
    }

    private static string Normalize(string content) => string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int EstimateTokenCount(string content) => Math.Max(1, content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 4 / 3);

    private static string NormalizeContentType(string? value)
    {
        var contentType = string.IsNullOrWhiteSpace(value) ? RagContentTypes.Listing : value.Trim().ToLowerInvariant();
        if (!RagContentTypes.IsSupported(contentType))
            throw new ArgumentException($"Unsupported RAG content type: {value}.", nameof(value));
        return contentType;
    }

    private static IReadOnlyDictionary<string, object?> DeserializeMetadata(string metadata) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(metadata) ?? new Dictionary<string, object?>();
}
