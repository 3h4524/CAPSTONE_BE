namespace APCS.Application.Features.SupportTickets.Dtos.Request;

/// <summary>Defines filtering and pagination for support-ticket lists.</summary>
public sealed record ListSupportTicketsRequestDto(
    int PageNumber = 1,
    int PageSize = 20,
    string? Status = null,
    string? Category = null,
    string? Priority = null);
