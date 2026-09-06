using APCS.Common.Models;
using APCS.Common.Constants;
using FluentAssertions;

namespace APCS.Common.UnitTests.Models;

[TestClass]
public sealed class ErrorAndPagingTests
{
    [TestMethod]
    public void ErrorFactories_WhenCalled_MapCodesTypesAndDetails()
    {
        var details = new Dictionary<string, string[]> { ["name"] = ["Required"] };

        Error.Validation("Invalid", details).Should().Be(
            new Error(ErrorCodes.Validation, "Invalid", ErrorType.Validation, details));
        Error.Unauthorized("unauthorized", "Unauthorized").Type.Should().Be(ErrorType.Unauthorized);
        Error.Forbidden("forbidden", "Forbidden").Type.Should().Be(ErrorType.Forbidden);
        Error.NotFound("not_found", "Missing").Type.Should().Be(ErrorType.NotFound);
        Error.Conflict("conflict", "Conflict").Type.Should().Be(ErrorType.Conflict);
        Error.Failure("failure", "Failure").Type.Should().Be(ErrorType.Failure);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 1)]
    [DataRow(10, 2)]
    [DataRow(11, 3)]
    public void TotalPages_ForCountAndPageSize_ReturnsCeiling(int totalCount, int expectedPages)
    {
        var result = new PagedResult<int>([], totalCount, 1, 5);

        result.TotalPages.Should().Be(expectedPages);
    }

    [TestMethod]
    public void TotalPages_WhenPageSizeIsZero_ReturnsZero()
    {
        new PagedResult<int>([], 10, 1, 0).TotalPages.Should().Be(0);
    }
}
