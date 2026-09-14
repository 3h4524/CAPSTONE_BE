using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Validators;

[TestClass]
public sealed class AdminResendTwoFactorValidatorTests
{
    [TestMethod]
    public void AdminResendTwoFactorValidator_WithValidRequest_IsValid()
    {
        new AdminResendTwoFactorValidator()
            .Validate(new AdminResendTwoFactorRequestDto("token", null))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    public void AdminResendTwoFactorValidator_WithInvalidRequest_ContainsExpectedProperty(
        string? tempToken)
    {
        var result = new AdminResendTwoFactorValidator()
            .Validate(new AdminResendTwoFactorRequestDto(tempToken!, null));

        result.Errors.Should().Contain(error => error.PropertyName == "TempToken");
    }
}
