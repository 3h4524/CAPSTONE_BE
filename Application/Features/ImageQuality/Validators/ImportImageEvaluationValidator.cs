using System.Text.RegularExpressions;
using APCS.Application.Abstractions.AI;
using APCS.Application.Features.ImageQuality.Common;
using APCS.Application.Features.ImageQuality.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.ImageQuality.Validators;

/// <summary>
/// Checks an uploaded scoring run before anything is stored: every image names a declared prompt version, ids are
/// unique and safe to use as storage keys, scores are real numbers and thumbnails are small images.
/// </summary>
public sealed partial class ImportImageEvaluationValidator : AbstractValidator<ImportImageEvaluationRequestDto>
{
    /// <summary>The most prompt versions one run may compare.</summary>
    public const int MaximumVersions = 50;

    /// <summary>The most images one run may hold.</summary>
    public const int MaximumImages = 1000;

    private static readonly string[] Phases = ["before", "after", "trial"];

    /// <summary>Creates the validator.</summary>
    public ImportImageEvaluationValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(3).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);

        RuleFor(x => x.Versions)
            .NotNull()
            .Must(versions => versions.Count is > 0 and <= MaximumVersions)
            .WithMessage($"A run lists between 1 and {MaximumVersions} prompt versions.")
            .Must(versions => versions.Select(version => version.Key).Distinct(StringComparer.Ordinal).Count() == versions.Count)
            .WithMessage("Two prompt versions share a key.");

        RuleForEach(x => x.Versions).ChildRules(version =>
        {
            version.RuleFor(x => x.Key).NotEmpty().MaximumLength(100);
            version.RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
            version.RuleFor(x => x.Provider)
                .Must(provider => ImageGenerationProviders.All.Contains(provider))
                .WithMessage("The provider must be gemini or ideogram.");
            version.RuleFor(x => x.Phase)
                .Must(phase => Phases.Contains(phase))
                .WithMessage("The phase must be before, after or trial.");
        }).When(x => x.Versions is not null);

        RuleFor(x => x.Images)
            .NotNull()
            .Must(images => images.Count is > 0 and <= MaximumImages)
            .WithMessage($"A run holds between 1 and {MaximumImages} images.")
            .Must(images => images.Select(image => image.Id).Distinct(StringComparer.Ordinal).Count() == images.Count)
            .WithMessage("Two images share an id.");

        RuleForEach(x => x.Images).ChildRules(image =>
        {
            image.RuleFor(x => x.Id)
                .NotEmpty()
                .MaximumLength(150)
                .Matches(SafeIdPattern())
                .WithMessage("An image id may hold only letters, digits, '-' and '_'.");
            image.RuleFor(x => x.Product).NotEmpty().MaximumLength(300);
            image.RuleFor(x => x.ImageUrl)
                .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                .When(x => x.ImageUrl is not null)
                .WithMessage("An image URL must be an absolute https URL.");
            image.RuleFor(x => x.Thumbnail)
                .Must(thumbnail => ImageEvalThumbnail.Decode(thumbnail) is not null)
                .When(x => x.Thumbnail is not null)
                .WithMessage($"A thumbnail must be a base64 JPEG, PNG or WebP image of at most {ImageEvalThumbnail.MaximumBytes / 1024} KB.");
            image.RuleFor(x => x)
                .Must(HasFiniteScores)
                .WithMessage("A score is not a finite number.");
        }).When(x => x.Images is not null);

        RuleForEach(x => x.Images)
            .Must((request, image) => request.Versions?.Any(version => version.Key == image.Version) == true)
            .WithMessage("An image names a prompt version the run does not list.")
            .When(x => x.Images is not null);
    }

    private static bool HasFiniteScores(ImageEvalImageRequestDto image) =>
        new[]
        {
            image.Cer, image.Musiq, image.TopiqNr, image.ClipIqaPlus, image.Liqe, image.LaionAes,
            image.ClipOverall, image.ClipElements,
        }.All(score => score is null || double.IsFinite(score.Value));

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SafeIdPattern();
}
