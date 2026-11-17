using APCS.Application.Features.Batches.Dtos;
using FluentValidation;

namespace APCS.Application.Features.Batches.Validators;

public sealed class SaveBatchValidator : AbstractValidator<SaveBatchDto>
{
    public SaveBatchValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.DefaultNiche).MaximumLength(255);
        // The product type sets the batch's product line, so every batch has one.
        RuleFor(x => x.DefaultProductType).Must(ProductRules.IsValidType)
            .WithMessage("Choose the product type of the batch: it sets which products the batch makes.");
    }
}
