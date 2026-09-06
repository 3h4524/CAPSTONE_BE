using APCS.Application.Features.Auth.Commands.Login;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Commands.Login;

[TestClass]
public sealed class LoginValidatorTests
{
    [TestMethod]
    public void LoginValidator_WithValidCommand_IsValid()
    {
        new LoginValidator()
            .Validate(new LoginCommand("seller@example.com", "Password1"))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("", "Password1", "Email")]
    [DataRow("invalid", "Password1", "Email")]
    [DataRow("seller@example.com", "", "Password")]
    public void LoginValidator_WithInvalidCommand_ContainsExpectedProperty(
        string email,
        string password,
        string propertyName)
    {
        var result = new LoginValidator().Validate(new LoginCommand(email, password));

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }
}
