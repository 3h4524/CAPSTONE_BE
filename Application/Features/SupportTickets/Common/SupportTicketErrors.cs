using APCS.Common.Models;

namespace APCS.Application.Features.SupportTickets.Common;

/// <summary>Creates expected errors returned by support-ticket use cases.</summary>
public static class SupportTicketErrors
{
    public static Error Unauthenticated() =>
        Error.Unauthorized("support_tickets.unauthorized", "The request is not authenticated.");

    public static Error NotFound() =>
        Error.NotFound("support_tickets.not_found", "The support ticket was not found.");

    public static Error InvalidTransition(string currentStatus, string requestedStatus) =>
        Error.Conflict(
            "support_tickets.invalid_status_transition",
            $"A ticket cannot move from '{currentStatus}' to '{requestedStatus}'.");

    public static Error ReplyNotAllowed() =>
        Error.Conflict(
            "support_tickets.reply_not_allowed",
            "Replies are not allowed after a ticket is resolved or closed.");

    public static Error RatingNotAllowed() =>
        Error.Conflict(
            "support_tickets.rating_not_allowed",
            "A satisfaction rating can only be submitted once for a resolved ticket.");

    public static Error InvalidAssignee() =>
        Error.Validation("The selected assignee must be an active administrator.");

    public static Error TicketNumberUnavailable() =>
        Error.Conflict(
            "support_tickets.number_unavailable",
            "A unique ticket number could not be generated. Please try again.");
}
