using APCS.Application.Features.SupportTickets.Common;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.SupportTickets.Validators;

/// <summary>Validates support-ticket list filtering and pagination.</summary>
public sealed class ListSupportTicketsValidator : AbstractValidator<ListSupportTicketsRequestDto>
{
    public ListSupportTicketsValidator()
    {
        RuleFor(request => request.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request.Status)
            .Must(status => status is null || SupportTicketStatuses.All.Contains(status));
        RuleFor(request => request.Category)
            .Must(category => category is null || SupportTicketCategories.All.Contains(category));
        RuleFor(request => request.Priority)
            .Must(priority => priority is null || SupportTicketPriorities.All.Contains(priority));
    }
}
