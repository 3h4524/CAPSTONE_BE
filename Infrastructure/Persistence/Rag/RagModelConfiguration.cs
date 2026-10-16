using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Rag;

internal static class RagModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RagDocument>(entity =>
        {
            entity.ToTable("rag_documents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
            entity.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(100).IsRequired();
            entity.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(200).IsRequired();
            entity.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(50).HasDefaultValue("listing").IsRequired();
            entity.Property(x => x.SourceUrl).HasColumnName("source_url");
            entity.Property(x => x.Version).HasColumnName("version").HasMaxLength(100);
            entity.Property(x => x.Language).HasColumnName("language").HasMaxLength(20).IsRequired();
            entity.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(128).IsRequired();
            entity.Property(x => x.IngestionContent).HasColumnName("ingestion_content").IsRequired();
            entity.Property(x => x.IngestionMetadata).HasColumnName("ingestion_metadata").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).HasDefaultValue("queued").IsRequired();
            entity.Property(x => x.IngestionError).HasColumnName("ingestion_error");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            // The same content may legitimately exist in multiple document versions.
            // Ingestion deduplication is handled by RagService so historical versions
            // can remain visible after a stale processing retry.
            entity.HasIndex(x => new { x.SourceId, x.ContentHash });
            entity.HasIndex(x => new { x.SourceId, x.Status });
        });

        modelBuilder.Entity<RagChunk>(entity =>
        {
            entity.ToTable("rag_chunks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(x => x.DocumentId).HasColumnName("document_id");
            entity.Property(x => x.Content).HasColumnName("content").IsRequired();
            entity.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(x => x.TokenCount).HasColumnName("token_count");
            entity.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();
            entity.HasOne(x => x.Document).WithMany(x => x.Chunks).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RagEmbedding>(entity =>
        {
            entity.ToTable("rag_embeddings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(x => x.ChunkId).HasColumnName("chunk_id");
            entity.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector").IsRequired();
            entity.Property(x => x.EmbeddingModel).HasColumnName("embedding_model").HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => new { x.ChunkId, x.EmbeddingModel }).IsUnique();
            entity.HasOne(x => x.Chunk).WithMany(x => x.Embeddings).HasForeignKey(x => x.ChunkId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
