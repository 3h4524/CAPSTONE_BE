using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Validators;

[TestClass]
public sealed class GoogleLoginValidatorTests
{
    [TestMethod]
    public void GoogleLoginValidator_WithValidRequest_IsValid()
    {
        new GoogleLoginValidator()
            .Validate(new GoogleLoginRequestDto("raw-id-token"))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void GoogleLoginValidator_WithEmptyIdToken_IsInvalid()
    {
        var result = new GoogleLoginValidator().Validate(new GoogleLoginRequestDto(""));

        result.Errors.Should().Contain(error => error.PropertyName == "IdToken");
    }
}
