using APCS.Common.Models;
using FluentAssertions;

namespace APCS.Common.UnitTests.Models;

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    public void Success_WhenCreated_HasNoError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [TestMethod]
    public void Failure_WhenCreated_ContainsExpectedError()
    {
        var error = Error.NotFound("sample.not_found", "Missing.");

        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [TestMethod]
    public void GenericSuccess_WhenValueRequested_ReturnsValue()
    {
        var result = Result.Success(42);

        result.Value.Should().Be(42);
    }

    [TestMethod]
    public void GenericFailure_WhenValueRequested_Throws()
    {
        var result = Result.Failure<int>(Error.Failure("sample.failed", "Failed."));

        var act = () => _ = result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Constructor_WhenSuccessContainsError_Throws()
    {
        var act = () => new TestResult(true, Error.Failure("sample.failed", "Failed."));

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Constructor_WhenFailureHasNoError_Throws()
    {
        var act = () => new TestResult(false, Error.None);

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class TestResult(bool isSuccess, Error error) : Result(isSuccess, error);
}
