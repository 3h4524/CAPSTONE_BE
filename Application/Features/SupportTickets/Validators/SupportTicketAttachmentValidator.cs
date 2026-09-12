using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.SupportTickets.Common;
using FluentValidation;

namespace APCS.Application.Features.SupportTickets.Validators;

/// <summary>Validates support-ticket upload metadata before external storage is called.</summary>
public sealed class SupportTicketAttachmentValidator : AbstractValidator<UploadFileDto>
{
    public SupportTicketAttachmentValidator()
    {
        RuleFor(file => file.FileName)
            .NotEmpty()
            .MaximumLength(255)
            .Must(HaveAllowedExtension)
            .WithMessage("The file extension is not supported.");

        RuleFor(file => file.ContentType)
            .NotEmpty()
            .Must(contentType => SupportTicketAttachmentRules.AllowedContentTypes.Contains(contentType))
            .WithMessage("The file content type is not supported.");

        RuleFor(file => file.Length)
            .GreaterThan(0)
            .LessThanOrEqualTo(SupportTicketAttachmentRules.MaximumFileBytes);
    }

    private static bool HaveAllowedExtension(string fileName) =>
        SupportTicketAttachmentRules.AllowedExtensions.Contains(
            Path.GetExtension(fileName).ToLowerInvariant());
}
