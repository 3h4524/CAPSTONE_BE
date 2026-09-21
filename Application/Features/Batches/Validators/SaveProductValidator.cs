using APCS.Application.Features.Batches.Dtos;
using FluentValidation;

namespace APCS.Application.Features.Batches.Validators;

public sealed class SaveProductValidator : AbstractValidator<SaveProductDto>
{
    public SaveProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ProductType).Must(ProductRules.IsValidType);
        RuleFor(x => x.Niche).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Keywords).NotNull();
        RuleFor(x => x.Keywords).Must(x => x is { Count: > 0 }).WithMessage("Enter at least one keyword.");
        RuleFor(x => x.Keywords).Must(x => x is { Count: <= 13 }).WithMessage("Enter no more than 13 keywords.");
        RuleForEach(x => x.Keywords).NotEmpty().MaximumLength(100).When(x => x.Keywords is not null);
        RuleFor(x => x.ProductDescription).MaximumLength(2000);
        RuleFor(x => x.SourceNotes).MaximumLength(2000);
    }
}

internal static class ProductRules
{
    private static readonly HashSet<string> Types = ["tshirt", "hoodie", "mug", "poster", "tote_bag", "phone_case"];
    public static bool IsValidType(string? type) => type is not null && Types.Contains(type);
}
