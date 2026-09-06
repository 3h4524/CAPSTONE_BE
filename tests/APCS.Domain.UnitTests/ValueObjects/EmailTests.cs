using APCS.Domain.Exceptions;
using APCS.Domain.ValueObjects;
using FluentAssertions;

namespace APCS.Domain.UnitTests.ValueObjects;

[TestClass]
public sealed class EmailTests
{
    [TestMethod]
    public void Create_WithValidEmail_NormalizesValue()
    {
        var email = Email.Create("  Seller@Example.COM ");

        email.Value.Should().Be("seller@example.com");
        email.ToString().Should().Be("seller@example.com");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_WithBlankEmail_ThrowsDomainException(string? value)
    {
        var act = () => Email.Create(value!);

        act.Should().Throw<DomainException>().WithMessage("*cannot be empty*");
    }

    [TestMethod]
    [DataRow("invalid")]
    [DataRow("missing-domain@")]
    [DataRow("@missing-local.com")]
    public void Create_WithInvalidEmail_ThrowsDomainException(string value)
    {
        var act = () => Email.Create(value);

        act.Should().Throw<DomainException>().WithMessage("*not valid*");
    }
}
