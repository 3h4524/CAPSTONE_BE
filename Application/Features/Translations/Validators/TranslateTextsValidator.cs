using APCS.Application.Features.Profile.Common;
using APCS.Application.Features.Translations.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Translations.Validators;

public sealed class TranslateTextsValidator : AbstractValidator<TranslateTextsRequestDto>
{
    public TranslateTextsValidator()
    {
        RuleFor(request => request.SourceLocale)
            .NotEmpty()
            .Must(IsSupportedLocale)
            .WithMessage("Source locale is not supported.");

        RuleFor(request => request.TargetLocale)
            .NotEmpty()
            .Must(IsSupportedLocale)
            .WithMessage("Target locale is not supported.");

        RuleFor(request => request.Items)
            .NotEmpty()
            .Must(items => items.Count <= 128)
            .WithMessage("A translation batch cannot contain more than 128 items.");

        RuleForEach(request => request.Items).ChildRules(item =>
        {
            item.RuleFor(value => value.Key).NotEmpty().MaximumLength(200);
            item.RuleFor(value => value.Text).NotEmpty().MaximumLength(10_000);
        });
    }

    private static bool IsSupportedLocale(string locale) =>
        ProfileOptions.Languages.Contains(locale.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
}
