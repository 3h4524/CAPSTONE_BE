using APCS.Application.Features.SupportTickets.Common;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.SupportTickets.Validators;

/// <summary>Validates Seller support-ticket replies.</summary>
public sealed class CreateTicketReplyValidator : AbstractValidator<CreateTicketReplyRequestDto>
{
    public CreateTicketReplyValidator()
    {
        RuleFor(request => request.ReplyText).NotEmpty().MaximumLength(5_000);
        RuleFor(request => request.Attachments)
            .NotNull()
            .Must(files => files.Count <= SupportTicketAttachmentRules.MaximumFileCount)
            .Must(files => files.Sum(file => file.Length) <= SupportTicketAttachmentRules.MaximumTotalBytes);
        RuleForEach(request => request.Attachments).SetValidator(new SupportTicketAttachmentValidator());
    }
}

/// <summary>Validates administrator support-ticket replies.</summary>
public sealed class CreateAdminTicketReplyValidator : AbstractValidator<CreateAdminTicketReplyRequestDto>
{
    public CreateAdminTicketReplyValidator()
    {
        RuleFor(request => request.ReplyText).NotEmpty().MaximumLength(5_000);
        RuleFor(request => request.Attachments)
            .NotNull()
            .Must(files => files.Count <= SupportTicketAttachmentRules.MaximumFileCount)
            .Must(files => files.Sum(file => file.Length) <= SupportTicketAttachmentRules.MaximumTotalBytes);
        RuleForEach(request => request.Attachments).SetValidator(new SupportTicketAttachmentValidator());
    }
}
