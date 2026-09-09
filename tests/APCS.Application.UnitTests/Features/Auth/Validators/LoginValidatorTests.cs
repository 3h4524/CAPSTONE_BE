using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Validators;

[TestClass]
public sealed class LoginValidatorTests
{
    [TestMethod]
    public void LoginValidator_WithValidRequest_IsValid()
    {
        new LoginValidator()
            .Validate(new LoginRequestDto("seller@example.com", "Password1"))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("", "Password1", "Email")]
    [DataRow("invalid", "Password1", "Email")]
    [DataRow("seller@example.com", "", "Password")]
    public void LoginValidator_WithInvalidRequest_ContainsExpectedProperty(
        string email,
        string password,
        string propertyName)
    {
        var result = new LoginValidator().Validate(new LoginRequestDto(email, password));

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }
}
