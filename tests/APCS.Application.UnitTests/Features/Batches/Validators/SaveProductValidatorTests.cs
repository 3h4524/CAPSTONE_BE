using APCS.Application.Features.Batches.Dtos;
using APCS.Application.Features.Batches.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Batches.Validators;

[TestClass]
public sealed class SaveProductValidatorTests
{
    private readonly SaveProductValidator validator = new();

    [TestMethod]
    public async Task Validate_WithAllRequiredFieldsAndThirteenKeywords_IsValid()
    {
        var result = await validator.ValidateAsync(new SaveProductDto(
            "Mountain sunrise tee",
            "tshirt",
            "hiking",
            Enumerable.Range(1, 13).Select(index => $"keyword-{index}").ToArray(),
            "A detailed design brief.",
            null));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public async Task Validate_WithFourteenKeywords_IsInvalid()
    {
        var result = await validator.ValidateAsync(new SaveProductDto(
            "Mountain sunrise tee",
            "tshirt",
            "hiking",
            Enumerable.Range(1, 14).Select(index => $"keyword-{index}").ToArray(),
            null,
            null));

        result.Errors.Should().Contain(error => error.PropertyName == "Keywords");
    }

    [TestMethod]
    public async Task Validate_WithoutNicheOrKeywords_IsInvalid()
    {
        var result = await validator.ValidateAsync(new SaveProductDto(
            "Mountain sunrise tee",
            "tshirt",
            null,
            [],
            null,
            null));

        result.Errors.Should().Contain(error => error.PropertyName == "Niche");
        result.Errors.Should().Contain(error => error.PropertyName == "Keywords");
    }

    [TestMethod]
    public async Task Validate_WithProductDescriptionLongerThanTwoThousandCharacters_IsInvalid()
    {
        var result = await validator.ValidateAsync(new SaveProductDto(
            "Mountain sunrise tee",
            "tshirt",
            "hiking",
            ["mountain"],
            new string('a', 2001),
            null));

        result.Errors.Should().Contain(error => error.PropertyName == "ProductDescription");
    }
}
