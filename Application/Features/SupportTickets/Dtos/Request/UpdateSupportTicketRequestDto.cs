namespace APCS.Application.Features.SupportTickets.Dtos.Request;

/// <summary>Requests an administrator lifecycle update to a support ticket.</summary>
public sealed record UpdateSupportTicketRequestDto(
    Guid? AssignedTo = null,
    string? Priority = null,
    string? Status = null);

/// <summary>Requests a one-time satisfaction rating for a resolved ticket.</summary>
public sealed record RateSupportTicketRequestDto(int Rating);
