namespace APCS.Application.Features.SupportTickets.Dtos.Response;

/// <summary>Represents a support ticket in a paged list.</summary>
public sealed record SupportTicketSummaryResponseDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Category,
    string Priority,
    string Status,
    int? SatisfactionRating,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
