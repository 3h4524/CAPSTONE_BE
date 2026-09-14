using APCS.Application.Features.SupportTickets.Common;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.SupportTickets.Validators;

/// <summary>Validates administrator lifecycle updates.</summary>
public sealed class UpdateSupportTicketValidator : AbstractValidator<UpdateSupportTicketRequestDto>
{
    public UpdateSupportTicketValidator()
    {
        RuleFor(request => request)
            .Must(request => request.AssignedTo is not null || request.Priority is not null || request.Status is not null)
            .WithMessage("At least one field must be supplied.");
        RuleFor(request => request.Priority)
            .Must(priority => priority is null || SupportTicketPriorities.All.Contains(priority));
        RuleFor(request => request.Status)
            .Must(status => status is null || SupportTicketStatuses.All.Contains(status));
    }
}

/// <summary>Validates a Seller satisfaction rating.</summary>
public sealed class RateSupportTicketValidator : AbstractValidator<RateSupportTicketRequestDto>
{
    public RateSupportTicketValidator()
    {
        RuleFor(request => request.Rating).InclusiveBetween(1, 5);
    }
}
