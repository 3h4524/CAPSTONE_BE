using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Admin.Moderation.Dtos;
using APCS.Application.Features.Moderation.Common;
using FluentValidation;

namespace APCS.Application.Features.Admin.Moderation.Validators;

public sealed class SaveModerationTermValidator : AbstractValidator<SaveModerationTermRequestDto>
{
    public SaveModerationTermValidator()
    {
        RuleFor(x => x.Term).NotEmpty().WithMessage("Enter a word or phrase.")
            .MaximumLength(100).WithMessage("Keep the term under 100 characters.");
        RuleFor(x => x.Category).Must(category => ModerationCategories.All.Contains(category))
            .WithMessage("Choose a supported category.");
        RuleFor(x => x.Severity).Must(severity => severity is "block" or "flag")
            .WithMessage("Severity must be block or flag.");
        RuleFor(x => x.MatchType).Must(type => type is "word" or "phrase")
            .WithMessage("Match type must be word or phrase.");
        RuleFor(x => x.Language).Must(language => language is "en" or "vi")
            .WithMessage("Language must be en or vi.");
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Keep the note under 300 characters.");
    }
}

public sealed class SaveModerationAllowlistValidator : AbstractValidator<SaveModerationAllowlistRequestDto>
{
    public SaveModerationAllowlistValidator()
    {
        RuleFor(x => x.Phrase).NotEmpty().WithMessage("Enter a phrase.")
            .MaximumLength(200).WithMessage("Keep the phrase under 200 characters.");
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Keep the note under 300 characters.");
    }
}

public sealed class UpdateModerationSettingsValidator : AbstractValidator<UpdateModerationSettingsRequestDto>
{
    public UpdateModerationSettingsValidator()
    {
        RuleFor(x => x.BlockAt).NotNull().WithMessage("Choose a threshold for each category.");
        RuleForEach(x => x.BlockAt)
            .Must(item => ModerationSettingKeys.AdjustableCategories.Contains(item.Key))
            .WithMessage(item => "This category's threshold cannot be changed.")
            .Must(item => item.Value is "high" or "medium")
            .WithMessage("A threshold must be high or medium.");
    }
}
