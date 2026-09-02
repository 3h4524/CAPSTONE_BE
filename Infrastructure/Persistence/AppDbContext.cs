using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Common;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Provides EF Core persistence for APCS.
/// </summary>
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    TimeProvider timeProvider)
    : IdentityDbContext<Seller, IdentityRole<int>, int>(options), IUnitOfWork, IReadDbContext
{
    public DbSet<Seller> Sellers => Set<Seller>();

    /// <inheritdoc />
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <inheritdoc />
    public DbSet<SellerProfile> SellerProfiles => Set<SellerProfile>();

    /// <inheritdoc />
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    /// <inheritdoc />
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();

    /// <inheritdoc />
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    /// <inheritdoc />
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();

    /// <inheritdoc />
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <inheritdoc />
    public DbSet<UsageStatistic> UsageStatistics => Set<UsageStatistic>();

    public DbSet<BatchJob> BatchJobs => Set<BatchJob>();
    public DbSet<BatchJobProduct> BatchJobProducts => Set<BatchJobProduct>();
    public DbSet<BatchJobLog> BatchJobLogs => Set<BatchJobLog>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<DesignTemplate> DesignTemplates => Set<DesignTemplate>();
    public DbSet<AiPrompt> AiPrompts => Set<AiPrompt>();
    public DbSet<DesignImage> DesignImages => Set<DesignImage>();
    public DbSet<MockupImage> MockupImages => Set<MockupImage>();
    public DbSet<VideoTemplate> VideoTemplates => Set<VideoTemplate>();
    public DbSet<MusicTrack> MusicTracks => Set<MusicTrack>();
    public DbSet<PromoVideo> PromoVideos => Set<PromoVideo>();
    public DbSet<ListingContent> ListingContents => Set<ListingContent>();
    public DbSet<ListingTitle> ListingTitles => Set<ListingTitle>();
    public DbSet<ListingTag> ListingTags => Set<ListingTag>();
    public DbSet<ListingDescription> ListingDescriptions => Set<ListingDescription>();
    public DbSet<SeoScore> SeoScores => Set<SeoScore>();
    public DbSet<ListingGenerationHistory> ListingGenerationHistory => Set<ListingGenerationHistory>();
    public DbSet<ExportPackage> ExportPackages => Set<ExportPackage>();
    public DbSet<PrintifyIntegration> PrintifyIntegrations => Set<PrintifyIntegration>();
    public DbSet<PrintifyUploadLog> PrintifyUploadLogs => Set<PrintifyUploadLog>();
    public DbSet<EtsyIntegration> EtsyIntegrations => Set<EtsyIntegration>();
    public DbSet<EtsyUploadLog> EtsyUploadLogs => Set<EtsyUploadLog>();
    public DbSet<SocialMediaShare> SocialMediaShares => Set<SocialMediaShare>();
    public DbSet<NotificationAlert> NotificationAlerts => Set<NotificationAlert>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<TicketReply> TicketReplies => Set<TicketReply>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemMetric> SystemMetrics => Set<SystemMetric>();

    IQueryable<SellerProfile> IReadDbContext.SellerProfiles => SellerProfiles.AsNoTracking();
    IQueryable<ApiKey> IReadDbContext.ApiKeys => ApiKeys.AsNoTracking();
    IQueryable<SubscriptionPlan> IReadDbContext.SubscriptionPlans => SubscriptionPlans.AsNoTracking();
    IQueryable<Subscription> IReadDbContext.Subscriptions => Subscriptions.AsNoTracking();
    IQueryable<PaymentMethod> IReadDbContext.PaymentMethods => PaymentMethods.AsNoTracking();
    IQueryable<Invoice> IReadDbContext.Invoices => Invoices.AsNoTracking();
    IQueryable<UsageStatistic> IReadDbContext.UsageStatistics => UsageStatistics.AsNoTracking();
    IQueryable<BatchJob> IReadDbContext.BatchJobs => BatchJobs.AsNoTracking();
    IQueryable<BatchJobProduct> IReadDbContext.BatchJobProducts => BatchJobProducts.AsNoTracking();
    IQueryable<BatchJobLog> IReadDbContext.BatchJobLogs => BatchJobLogs.AsNoTracking();
    IQueryable<Product> IReadDbContext.Products => Products.AsNoTracking();
    IQueryable<DesignTemplate> IReadDbContext.DesignTemplates => DesignTemplates.AsNoTracking();
    IQueryable<AiPrompt> IReadDbContext.AiPrompts => AiPrompts.AsNoTracking();
    IQueryable<DesignImage> IReadDbContext.DesignImages => DesignImages.AsNoTracking();
    IQueryable<MockupImage> IReadDbContext.MockupImages => MockupImages.AsNoTracking();
    IQueryable<VideoTemplate> IReadDbContext.VideoTemplates => VideoTemplates.AsNoTracking();
    IQueryable<MusicTrack> IReadDbContext.MusicTracks => MusicTracks.AsNoTracking();
    IQueryable<PromoVideo> IReadDbContext.PromoVideos => PromoVideos.AsNoTracking();
    IQueryable<ListingContent> IReadDbContext.ListingContents => ListingContents.AsNoTracking();
    IQueryable<ListingTitle> IReadDbContext.ListingTitles => ListingTitles.AsNoTracking();
    IQueryable<ListingTag> IReadDbContext.ListingTags => ListingTags.AsNoTracking();
    IQueryable<ListingDescription> IReadDbContext.ListingDescriptions => ListingDescriptions.AsNoTracking();
    IQueryable<SeoScore> IReadDbContext.SeoScores => SeoScores.AsNoTracking();
    IQueryable<ListingGenerationHistory> IReadDbContext.ListingGenerationHistory => ListingGenerationHistory.AsNoTracking();
    IQueryable<ExportPackage> IReadDbContext.ExportPackages => ExportPackages.AsNoTracking();
    IQueryable<PrintifyIntegration> IReadDbContext.PrintifyIntegrations => PrintifyIntegrations.AsNoTracking();
    IQueryable<PrintifyUploadLog> IReadDbContext.PrintifyUploadLogs => PrintifyUploadLogs.AsNoTracking();
    IQueryable<EtsyIntegration> IReadDbContext.EtsyIntegrations => EtsyIntegrations.AsNoTracking();
    IQueryable<EtsyUploadLog> IReadDbContext.EtsyUploadLogs => EtsyUploadLogs.AsNoTracking();
    IQueryable<SocialMediaShare> IReadDbContext.SocialMediaShares => SocialMediaShares.AsNoTracking();
    IQueryable<NotificationAlert> IReadDbContext.NotificationAlerts => NotificationAlerts.AsNoTracking();
    IQueryable<SupportTicket> IReadDbContext.SupportTickets => SupportTickets.AsNoTracking();
    IQueryable<TicketReply> IReadDbContext.TicketReplies => TicketReplies.AsNoTracking();
    IQueryable<Admin> IReadDbContext.Admins => Admins.AsNoTracking();
    IQueryable<SystemConfiguration> IReadDbContext.SystemConfigurations => SystemConfigurations.AsNoTracking();
    IQueryable<AuditLog> IReadDbContext.AuditLogs => AuditLogs.AsNoTracking();
    IQueryable<SystemMetric> IReadDbContext.SystemMetrics => SystemMetrics.AsNoTracking();

    /// <inheritdoc />
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);
        return new AppDbTransaction(transaction);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureIdentityTableNames(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    private static void ConfigureIdentityTableNames(ModelBuilder builder)
    {
        builder.Entity<Seller>().ToTable("sellers");
        builder.Entity<IdentityRole<int>>().ToTable("roles");
        builder.Entity<IdentityUserRole<int>>().ToTable("seller_roles");
        builder.Entity<IdentityUserClaim<int>>().ToTable("seller_claims");
        builder.Entity<IdentityUserLogin<int>>().ToTable("seller_logins");
        builder.Entity<IdentityRoleClaim<int>>().ToTable("role_claims");
        builder.Entity<IdentityUserToken<int>>().ToTable("seller_tokens");

        builder.Entity<IdentityUserRole<int>>()
            .Property(entity => entity.UserId)
            .HasColumnName("seller_id");

        builder.Entity<IdentityUserClaim<int>>()
            .Property(entity => entity.UserId)
            .HasColumnName("seller_id");

        builder.Entity<IdentityUserLogin<int>>()
            .Property(entity => entity.UserId)
            .HasColumnName("seller_id");

        builder.Entity<IdentityUserToken<int>>()
            .Property(entity => entity.UserId)
            .HasColumnName("seller_id");
    }

    private void ApplyAuditTimestamps()
    {
        var utcNow = timeProvider.GetUtcNow();

        foreach (var entry in ChangeTracker.Entries<IHasCreationTime>().Where(entry => entry.State == EntityState.Added))
        {
            var property = entry.Property(nameof(IHasCreationTime.CreatedAtUtc));
            if (property.CurrentValue is not DateTimeOffset createdAt || createdAt == default)
            {
                property.CurrentValue = utcNow;
            }
        }

        foreach (var entry in ChangeTracker.Entries<IHasModificationTime>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property(nameof(IHasModificationTime.UpdatedAtUtc)).CurrentValue = utcNow;
        }
    }

    private sealed class AppDbTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
