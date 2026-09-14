using APCS.Application.Features.Profile.Dtos.Request;
using APCS.Application.Features.Profile.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Profile.Validators;

[TestClass]
public sealed class UpdateProfileValidatorTests
{
    [TestMethod]
    public void UpdateProfileValidator_WithEmptyPatch_IsValid()
    {
        new UpdateProfileValidator()
            .Validate(new UpdateProfileRequestDto())
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void UpdateProfileValidator_WithValidRequest_IsValid()
    {
        var request = new UpdateProfileRequestDto(
            FullName: "Seller Name",
            Email: "seller@example.com",
            ShopName: "Shop Name",
            ShopDescription: "Short description",
            Timezone: "Asia/Ho_Chi_Minh",
            Language: "vi",
            ThemePreference: "system",
            NotificationEmailEnabled: true,
            NewsletterSubscribed: false,
            TwoFactorEnabled: false);

        new UpdateProfileValidator().Validate(request).IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("", "FullName")]
    [DataRow("", "Email")]
    public void UpdateProfileValidator_WithInvalidScalar_ContainsExpectedProperty(
        string value,
        string propertyName)
    {
        var request = propertyName == "FullName"
            ? new UpdateProfileRequestDto(FullName: value)
            : new UpdateProfileRequestDto(Email: value);

        var result = new UpdateProfileValidator().Validate(request);

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }

    [TestMethod]
    public void UpdateProfileValidator_WithMalformedEmail_ContainsEmail()
    {
        var result = new UpdateProfileValidator()
            .Validate(new UpdateProfileRequestDto(Email: "not-an-email"));

        result.Errors.Should().Contain(error => error.PropertyName == "Email");
    }

    [TestMethod]
    [DataRow("FullName", 101)]
    [DataRow("ShopName", 101)]
    [DataRow("ShopDescription", 101)]
    public void UpdateProfileValidator_WhenTextExceedsMaximumLength_IsInvalid(
        string propertyName,
        int length)
    {
        var value = new string('a', length);
        var request = propertyName switch
        {
            "FullName" => new UpdateProfileRequestDto(FullName: value),
            "ShopName" => new UpdateProfileRequestDto(ShopName: value),
            _ => new UpdateProfileRequestDto(ShopDescription: value)
        };

        var result = new UpdateProfileValidator().Validate(request);

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }

    [TestMethod]
    [DataRow("Mars/Olympus", "Timezone")]
    [DataRow("fr", "Language")]
    [DataRow("neon", "ThemePreference")]
    public void UpdateProfileValidator_WhenPreferenceIsNotAllowlisted_IsInvalid(
        string value,
        string propertyName)
    {
        var request = propertyName switch
        {
            "Timezone" => new UpdateProfileRequestDto(Timezone: value),
            "Language" => new UpdateProfileRequestDto(Language: value),
            _ => new UpdateProfileRequestDto(ThemePreference: value)
        };

        var result = new UpdateProfileValidator().Validate(request);

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }
}
