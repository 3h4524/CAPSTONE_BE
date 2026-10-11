using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<RagDocument> RagDocuments => Set<RagDocument>();
    public DbSet<RagChunk> RagChunks => Set<RagChunk>();
    public DbSet<RagEmbedding> RagEmbeddings => Set<RagEmbedding>();
}
