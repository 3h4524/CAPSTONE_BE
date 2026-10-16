using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.IntegrationTests;

internal sealed class RagFreeAppDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Ignore<RagEmbedding>();
    }
}
