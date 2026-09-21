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
        RuleFor(x => x.DefaultProductType).Must(ProductRules.IsValidType).When(x => !string.IsNullOrWhiteSpace(x.DefaultProductType));
    }
}
