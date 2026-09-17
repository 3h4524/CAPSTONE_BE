using APCS.Application.Features.SupportTickets.Common;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.SupportTickets.Validators;

/// <summary>Validates Seller support-ticket creation.</summary>
public sealed class CreateSupportTicketValidator : AbstractValidator<CreateSupportTicketRequestDto>
{
    public CreateSupportTicketValidator()
    {
        RuleFor(request => request.Subject).NotEmpty().MaximumLength(255);
        RuleFor(request => request.Description).NotEmpty().MaximumLength(10_000);
        RuleFor(request => request.Category)
            .Must(category => SupportTicketCategories.All.Contains(category))
            .WithMessage("The selected category is invalid.");
        RuleFor(request => request.Priority)
            .Must(priority => SupportTicketPriorities.All.Contains(priority))
            .WithMessage("The selected priority is invalid.");
        RuleFor(request => request.Attachments)
            .NotNull()
            .Must(files => files.Count <= SupportTicketAttachmentRules.MaximumFileCount)
            .WithMessage($"A ticket can contain at most {SupportTicketAttachmentRules.MaximumFileCount} attachments.")
            .Must(files => files.Sum(file => file.Length) <= SupportTicketAttachmentRules.MaximumTotalBytes)
            .WithMessage("The combined attachment size cannot exceed 25 MB.");
        RuleForEach(request => request.Attachments).SetValidator(new SupportTicketAttachmentValidator());
    }
}
