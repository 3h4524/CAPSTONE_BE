using APCS.Common.Extensions;
using FluentAssertions;

namespace APCS.Common.UnitTests.Extensions;

[TestClass]
public sealed class EnumerableAndStringExtensionsTests
{
    [TestMethod]
    public void ToPagedResult_WithValidPage_ReturnsExpectedSliceAndMetadata()
    {
        var result = Enumerable.Range(1, 12).ToPagedResult(2, 5);

        result.Items.Should().Equal(6, 7, 8, 9, 10);
        result.TotalCount.Should().Be(12);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(5);
        result.TotalPages.Should().Be(3);
    }

    [TestMethod]
    public void ToPagedResult_WithOutOfRangeInputs_ClampsInputs()
    {
        var result = Enumerable.Range(1, 250).ToPagedResult(0, 500);

        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(200);
        result.Items.Should().HaveCount(200);
    }

    [TestMethod]
    public void ToPagedResult_WithNullSource_Throws()
    {
        IEnumerable<int> source = null!;

        var act = () => source.ToPagedResult(1, 10);

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    [DataRow(null, true)]
    [DataRow("", true)]
    [DataRow("  ", true)]
    [DataRow("value", false)]
    public void IsNullOrWhiteSpace_ForInput_ReturnsExpected(string? value, bool expected)
    {
        value.IsNullOrWhiteSpace().Should().Be(expected);
    }

    [TestMethod]
    public void Truncate_WhenValueIsLonger_ReturnsRequestedLength()
    {
        "abcdef".Truncate(3).Should().Be("abc");
    }

    [TestMethod]
    public void Truncate_WhenLengthIsNegative_Throws()
    {
        var act = () => "value".Truncate(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    [DataRow("  Hello,   Clean-World!  ", "hello-clean-world")]
    [DataRow("---A__B---", "ab")]
    [DataRow("", "")]
    public void ToSlug_ForInput_ReturnsNormalizedSlug(string value, string expected)
    {
        value.ToSlug().Should().Be(expected);
    }
}
