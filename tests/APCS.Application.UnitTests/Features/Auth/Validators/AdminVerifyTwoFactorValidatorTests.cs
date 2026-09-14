using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Validators;

[TestClass]
public sealed class AdminVerifyTwoFactorValidatorTests
{
    [TestMethod]
    public void AdminVerifyTwoFactorValidator_WithValidRequest_IsValid()
    {
        new AdminVerifyTwoFactorValidator()
            .Validate(new AdminVerifyTwoFactorRequestDto("token", "123456"))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("", "123456", "TempToken")]
    [DataRow(null, "123456", "TempToken")]
    [DataRow("token", "", "OtpCode")]
    [DataRow("token", "12345", "OtpCode")]
    [DataRow("token", "1234567", "OtpCode")]
    [DataRow("token", "abcdef", "OtpCode")]
    public void AdminVerifyTwoFactorValidator_WithInvalidRequest_ContainsExpectedProperty(
        string? tempToken,
        string? otpCode,
        string propertyName)
    {
        var result = new AdminVerifyTwoFactorValidator()
            .Validate(new AdminVerifyTwoFactorRequestDto(tempToken!, otpCode!));

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }
}
