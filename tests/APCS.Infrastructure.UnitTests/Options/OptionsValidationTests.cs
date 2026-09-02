using System.ComponentModel.DataAnnotations;
using APCS.Infrastructure.Options;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Options;

[TestClass]
public sealed class OptionsValidationTests
{
    [TestMethod]
    public void JwtOptions_WithValidValues_PassesDataAnnotations()
    {
        var options = new JwtOptions { SigningKey = new string('k', 32) };

        Validate(options).Should().BeEmpty();
    }

    [TestMethod]
    public void JwtOptions_WithInvalidLifetimesAndKey_ReturnsValidationErrors()
    {
        var options = new JwtOptions
        {
            SigningKey = "short",
            AccessTokenMinutes = 0,
            RefreshTokenDays = 366
        };

        Validate(options).Should().HaveCount(3);
    }

    [TestMethod]
    public void RedisOptions_WithMissingConnectionAndInvalidExpiration_ReturnsValidationErrors()
    {
        var options = new RedisOptions { ConnectionString = "", DefaultExpirationMinutes = 0 };

        Validate(options).Should().HaveCount(2);
    }

    private static List<ValidationResult> Validate(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
