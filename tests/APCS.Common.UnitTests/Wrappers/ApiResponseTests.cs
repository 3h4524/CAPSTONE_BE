using APCS.Common.Wrappers;
using FluentAssertions;

namespace APCS.Common.UnitTests.Wrappers;

[TestClass]
public sealed class ApiResponseTests
{
    [TestMethod]
    public void Success_WithValue_CreatesSuccessfulEnvelope()
    {
        var response = ApiResponse<int>.Success(42, "Created");

        response.Succeeded.Should().BeTrue();
        response.Data.Should().Be(42);
        response.Message.Should().Be("Created");
        response.Errors.Should().BeNull();
    }

    [TestMethod]
    public void Failure_WithErrors_CreatesFailedEnvelope()
    {
        var response = ApiResponse<int>.Failure(["Invalid"], "Failed");

        response.Succeeded.Should().BeFalse();
        response.Data.Should().Be(0);
        response.Message.Should().Be("Failed");
        response.Errors.Should().Equal("Invalid");
    }
}
