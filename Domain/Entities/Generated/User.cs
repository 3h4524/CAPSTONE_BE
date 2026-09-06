using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string? PasswordHash { get; set; }

    public string FullName { get; set; } = null!;

    public string? OauthGoogleId { get; set; }

    public string? OauthProvider { get; set; }

    public string? AvatarUrl { get; set; }

    public string AccountStatus { get; set; } = null!;

    public bool? EmailVerified { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();

    public virtual ICollection<ApiUsageRecord> ApiUsageRecords { get; set; } = new List<ApiUsageRecord>();

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<AuthToken> AuthTokens { get; set; } = new List<AuthToken>();

    public virtual ICollection<BatchJob> BatchJobs { get; set; } = new List<BatchJob>();

    public virtual ICollection<DesignTemplate> DesignTemplates { get; set; } = new List<DesignTemplate>();

    public virtual ICollection<EtsyIntegration> EtsyIntegrations { get; set; } = new List<EtsyIntegration>();

    public virtual ICollection<ExportPackage> ExportPackages { get; set; } = new List<ExportPackage>();

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<NotificationAlert> NotificationAlerts { get; set; } = new List<NotificationAlert>();

    public virtual ICollection<PaymentMethod> PaymentMethods { get; set; } = new List<PaymentMethod>();

    public virtual ICollection<PrintifyIntegration> PrintifyIntegrations { get; set; } = new List<PrintifyIntegration>();

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual ICollection<SupportTicket> SupportTicketAssignedToNavigations { get; set; } = new List<SupportTicket>();

    public virtual ICollection<SupportTicket> SupportTicketUsers { get; set; } = new List<SupportTicket>();

    public virtual ICollection<TicketAttachment> TicketAttachments { get; set; } = new List<TicketAttachment>();

    public virtual ICollection<TicketReply> TicketReplies { get; set; } = new List<TicketReply>();

    public virtual ICollection<UsageStatistic> UsageStatistics { get; set; } = new List<UsageStatistic>();

    public virtual UserProfile? UserProfile { get; set; }

    public virtual ICollection<UserRole> UserRoleGrantedByNavigations { get; set; } = new List<UserRole>();

    public virtual ICollection<UserRole> UserRoleUsers { get; set; } = new List<UserRole>();
}
