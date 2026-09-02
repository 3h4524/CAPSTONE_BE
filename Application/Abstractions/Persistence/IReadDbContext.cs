using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Exposes read-only query roots for application queries.
/// </summary>
public interface IReadDbContext
{
    IQueryable<SellerProfile> SellerProfiles { get; }

    IQueryable<ApiKey> ApiKeys { get; }

    IQueryable<SubscriptionPlan> SubscriptionPlans { get; }

    IQueryable<Subscription> Subscriptions { get; }

    IQueryable<PaymentMethod> PaymentMethods { get; }

    IQueryable<Invoice> Invoices { get; }

    IQueryable<UsageStatistic> UsageStatistics { get; }

    IQueryable<BatchJob> BatchJobs { get; }
    IQueryable<BatchJobProduct> BatchJobProducts { get; }
    IQueryable<BatchJobLog> BatchJobLogs { get; }
    IQueryable<Product> Products { get; }
    IQueryable<DesignTemplate> DesignTemplates { get; }
    IQueryable<AiPrompt> AiPrompts { get; }
    IQueryable<DesignImage> DesignImages { get; }
    IQueryable<MockupImage> MockupImages { get; }
    IQueryable<VideoTemplate> VideoTemplates { get; }
    IQueryable<MusicTrack> MusicTracks { get; }
    IQueryable<PromoVideo> PromoVideos { get; }
    IQueryable<ListingContent> ListingContents { get; }
    IQueryable<ListingTitle> ListingTitles { get; }
    IQueryable<ListingTag> ListingTags { get; }
    IQueryable<ListingDescription> ListingDescriptions { get; }
    IQueryable<SeoScore> SeoScores { get; }
    IQueryable<ListingGenerationHistory> ListingGenerationHistory { get; }
    IQueryable<ExportPackage> ExportPackages { get; }
    IQueryable<PrintifyIntegration> PrintifyIntegrations { get; }
    IQueryable<PrintifyUploadLog> PrintifyUploadLogs { get; }
    IQueryable<EtsyIntegration> EtsyIntegrations { get; }
    IQueryable<EtsyUploadLog> EtsyUploadLogs { get; }
    IQueryable<SocialMediaShare> SocialMediaShares { get; }
    IQueryable<NotificationAlert> NotificationAlerts { get; }
    IQueryable<SupportTicket> SupportTickets { get; }
    IQueryable<TicketReply> TicketReplies { get; }
    IQueryable<Admin> Admins { get; }
    IQueryable<SystemConfiguration> SystemConfigurations { get; }
    IQueryable<AuditLog> AuditLogs { get; }
    IQueryable<SystemMetric> SystemMetrics { get; }
}
