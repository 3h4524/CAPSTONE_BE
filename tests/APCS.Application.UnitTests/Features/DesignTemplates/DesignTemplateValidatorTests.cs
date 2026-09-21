using APCS.Application.Features.DesignTemplates.Common;
using APCS.Application.Features.DesignTemplates.Dtos.Request;
using APCS.Application.Features.DesignTemplates.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.DesignTemplates;

[TestClass]
public sealed class DesignTemplateValidatorTests
{
    [TestMethod]
    public async Task CreateValidator_WithMaximumPromptAndFiveExamples_IsValid()
    {
        var request = ValidCreate(new string('a', DesignTemplateRules.MaximumBasePromptLength),
            Enumerable.Range(1, DesignTemplateRules.MaximumExamples)
                .Select(index => new DesignTemplateExampleDto($"Subject {index}", $"Prompt {index}"))
                .ToArray());

        var result = await new CreateDesignTemplateValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateValidator_WithPromptLongerThanMaximum_IsInvalid()
    {
        var request = ValidCreate(new string('a', DesignTemplateRules.MaximumBasePromptLength + 1), []);

        var result = await new CreateDesignTemplateValidator().ValidateAsync(request);

        result.Errors.Should().ContainSingle(error => error.PropertyName == nameof(request.BasePrompt));
    }

    [TestMethod]
    public async Task CreateValidator_WithUnsupportedPlaceholder_IsInvalid()
    {
        var request = ValidCreate("Create {subject} with {unsupported}.", []);

        var result = await new CreateDesignTemplateValidator().ValidateAsync(request);

        result.Errors.Should().ContainSingle(error => error.PropertyName == nameof(request.BasePrompt));
    }

    [TestMethod]
    public async Task CreateValidator_WithEmptyNegativePrompt_IsValid()
    {
        var request = new CreateDesignTemplateRequestDto(
            "My template",
            DesignTemplateNiches.NatureBotanical,
            DesignTemplateArtStyles.Watercolor,
            "Create {subject}.",
            null,
            []);

        var result = await new CreateDesignTemplateValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateValidator_WithUnsupportedNegativePromptPlaceholder_IsInvalid()
    {
        var request = new CreateDesignTemplateRequestDto(
            "My template",
            DesignTemplateNiches.NatureBotanical,
            DesignTemplateArtStyles.Watercolor,
            "Create {subject}.",
            "Avoid {unsupported}.",
            []);

        var result = await new CreateDesignTemplateValidator().ValidateAsync(request);

        result.Errors.Should().ContainSingle(error => error.PropertyName == nameof(request.NegativePrompt));
    }

    [TestMethod]
    public async Task ListValidator_WithInvalidPageAndScope_IsInvalid()
    {
        var request = new ListDesignTemplatesRequestDto(0, 49, Scope: "foreign");

        var result = await new ListDesignTemplatesValidator().ValidateAsync(request);

        result.Errors.Select(error => error.PropertyName).Should().BeEquivalentTo(
            nameof(request.PageNumber),
            nameof(request.PageSize),
            nameof(request.Scope));
    }

    [TestMethod]
    [DataRow(DesignTemplateScopes.System)]
    [DataRow(DesignTemplateScopes.Personal)]
    public async Task ListValidator_WithLibraryTabScope_IsValid(string scope)
    {
        var request = new ListDesignTemplatesRequestDto(Scope: scope);

        var result = await new ListDesignTemplatesValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    private static CreateDesignTemplateRequestDto ValidCreate(
        string prompt,
        IReadOnlyList<DesignTemplateExampleDto> examples) =>
        new(
            "My template",
            DesignTemplateNiches.NatureBotanical,
            DesignTemplateArtStyles.Watercolor,
            prompt,
            "logo, watermark",
            examples);
}
