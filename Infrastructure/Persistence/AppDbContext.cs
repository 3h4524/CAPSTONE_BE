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
    : IdentityDbContext<
        User,
        Role,
        Guid,
        IdentityUserClaim<Guid>,
        UserRole,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>(options),
      IUnitOfWork,
      IReadDbContext
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserRoleHistory> UserRoleHistory => Set<UserRoleHistory>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<UsageStatistic> UsageStatistics => Set<UsageStatistic>();
    public DbSet<ApiUsageRecord> ApiUsageRecords => Set<ApiUsageRecord>();

    public DbSet<BatchJob> BatchJobs => Set<BatchJob>();
    public DbSet<BatchJobProduct> BatchJobProducts => Set<BatchJobProduct>();
    public DbSet<BatchJobLog> BatchJobLogs => Set<BatchJobLog>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<DesignTemplate> DesignTemplates => Set<DesignTemplate>();
    public DbSet<MockupTemplate> MockupTemplates => Set<MockupTemplate>();
    public DbSet<ProductMockupTemplate> ProductMockupTemplates => Set<ProductMockupTemplate>();
    public DbSet<AiPrompt> AiPrompts => Set<AiPrompt>();
    public DbSet<DesignImage> DesignImages => Set<DesignImage>();
    public DbSet<MockupImage> MockupImages => Set<MockupImage>();

    public DbSet<VideoTemplate> VideoTemplates => Set<VideoTemplate>();
    public DbSet<MusicTrack> MusicTracks => Set<MusicTrack>();
    public DbSet<PromoVideo> PromoVideos => Set<PromoVideo>();
    public DbSet<PromoVideoScene> PromoVideoScenes => Set<PromoVideoScene>();

    public DbSet<ListingContent> ListingContents => Set<ListingContent>();
    public DbSet<ListingTitle> ListingTitles => Set<ListingTitle>();
    public DbSet<ListingTag> ListingTags => Set<ListingTag>();
    public DbSet<ListingTagItem> ListingTagItems => Set<ListingTagItem>();
    public DbSet<ListingDescription> ListingDescriptions => Set<ListingDescription>();
    public DbSet<SeoScore> SeoScores => Set<SeoScore>();
    public DbSet<ListingGenerationHistory> ListingGenerationHistory => Set<ListingGenerationHistory>();

    public DbSet<ExportPackage> ExportPackages => Set<ExportPackage>();
    public DbSet<ExportPackageItem> ExportPackageItems => Set<ExportPackageItem>();

    public DbSet<PrintifyIntegration> PrintifyIntegrations => Set<PrintifyIntegration>();
    public DbSet<PrintifyUploadLog> PrintifyUploadLogs => Set<PrintifyUploadLog>();
    public DbSet<EtsyIntegration> EtsyIntegrations => Set<EtsyIntegration>();
    public DbSet<EtsyUploadLog> EtsyUploadLogs => Set<EtsyUploadLog>();
    public DbSet<SocialMediaShare> SocialMediaShares => Set<SocialMediaShare>();
    public DbSet<ShareHashtag> ShareHashtags => Set<ShareHashtag>();

    public DbSet<NotificationAlert> NotificationAlerts => Set<NotificationAlert>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<TicketReply> TicketReplies => Set<TicketReply>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <inheritdoc />
    public IQueryable<TEntity> Query<TEntity>()
        where TEntity : class =>
        Set<TEntity>().AsNoTracking();

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
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    /// <remarks>
    /// This stamps every tracked entity that declares a modification time, including
    /// <see cref="User"/>. Identity writes to the user row for its own bookkeeping — a failed
    /// sign-in bumping the lockout counter, for instance — so <c>UpdatedAtUtc</c> means
    /// "the row changed", not "the person edited their profile".
    /// </remarks>
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
