namespace APCS.Application.Features.SupportTickets.Common;

/// <summary>Provides the format and generation limits for support-ticket numbers.</summary>
internal static class SupportTicketNumberRules
{
    public const string Prefix = "APCS";
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int SuffixLength = 6;
    public const int MaximumGenerationAttempts = 5;
}

/// <summary>Provides the persisted support-ticket status values.</summary>
public static class SupportTicketStatuses
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string WaitingCustomer = "waiting_customer";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static IReadOnlyCollection<string> All { get; } =
        [Open, InProgress, WaitingCustomer, Resolved, Closed];
}

/// <summary>Provides the persisted support-ticket priority values.</summary>
public static class SupportTicketPriorities
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";
    public const string Urgent = "urgent";

    public static IReadOnlyCollection<string> All { get; } = [Low, Normal, High, Urgent];
}

/// <summary>Provides the categories accepted by the Seller support experience.</summary>
public static class SupportTicketCategories
{
    public const string Integration = "integration";
    public const string AiGeneration = "ai_generation";
    public const string BatchProcessing = "batch_processing";
    public const string ExportPublishing = "export_publishing";
    public const string BillingSubscription = "billing_subscription";
    public const string AccountSecurity = "account_security";
    public const string Other = "other";

    public static IReadOnlyCollection<string> All { get; } =
    [
        Integration,
        AiGeneration,
        BatchProcessing,
        ExportPublishing,
        BillingSubscription,
        AccountSecurity,
        Other
    ];
}

/// <summary>Provides attachment limits shared by ticket and reply validation.</summary>
public static class SupportTicketAttachmentRules
{
    public const int MaximumFileCount = 5;
    public const long MaximumFileBytes = 10 * 1024 * 1024;
    public const long MaximumTotalBytes = 25 * 1024 * 1024;

    public static IReadOnlyCollection<string> AllowedExtensions { get; } =
        [".jpg", ".jpeg", ".png", ".webp", ".pdf", ".txt", ".log"];

    public static IReadOnlyCollection<string> AllowedContentTypes { get; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf",
        "text/plain",
        "application/octet-stream"
    ];
}
