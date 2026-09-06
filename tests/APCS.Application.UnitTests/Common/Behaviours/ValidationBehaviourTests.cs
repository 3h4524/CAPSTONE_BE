using APCS.Application.Common.Behaviours;
using APCS.Common.Models;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;

namespace APCS.Application.UnitTests.Common.Behaviours;

[TestClass]
public sealed class ValidationBehaviourTests
{
    [TestMethod]
    public async Task Handle_WithoutValidators_InvokesNext()
    {
        var behaviour = new ValidationBehaviour<TestRequest, Result<string>>([]);
        var nextCalls = 0;

        var result = await behaviour.Handle(
            new TestRequest("value"),
            () =>
            {
                nextCalls++;
                return Task.FromResult(Result.Success("handled"));
            },
            CancellationToken.None);

        result.Value.Should().Be("handled");
        nextCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task Handle_WhenValid_InvokesNext()
    {
        var behaviour = new ValidationBehaviour<TestRequest, Result<string>>(
            [new InlineValidator<TestRequest>()]);

        var result = await behaviour.Handle(
            new TestRequest("value"),
            () => Task.FromResult(Result.Success("handled")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task Handle_WhenInvalid_ReturnsGroupedGenericFailureWithoutInvokingNext()
    {
        var first = new InlineValidator<TestRequest>();
        first.RuleFor(request => request.Value).NotEmpty().WithMessage("Required");
        var second = new DuplicateFailureValidator();
        var behaviour = new ValidationBehaviour<TestRequest, Result<string>>([first, second]);
        var nextCalled = false;

        var result = await behaviour.Handle(
            new TestRequest(string.Empty),
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("handled"));
            },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Details![nameof(TestRequest.Value)].Should().Equal("Required");
        nextCalled.Should().BeFalse();
    }

    [TestMethod]
    public async Task Handle_WhenInvalidAndResponseIsResult_ReturnsFailure()
    {
        var validator = new InlineValidator<TestRequest>();
        validator.RuleFor(request => request.Value).NotEmpty();
        var behaviour = new ValidationBehaviour<TestRequest, Result>([validator]);

        var result = await behaviour.Handle(
            new TestRequest(string.Empty),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [TestMethod]
    public async Task Handle_WhenInvalidAndResponseIsUnsupported_ThrowsValidationException()
    {
        var validator = new InlineValidator<TestRequest>();
        validator.RuleFor(request => request.Value).NotEmpty();
        var behaviour = new ValidationBehaviour<TestRequest, string>([validator]);

        var act = () => behaviour.Handle(
            new TestRequest(string.Empty),
            () => Task.FromResult("handled"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed record TestRequest(string Value);

    private sealed class DuplicateFailureValidator : AbstractValidator<TestRequest>
    {
        public override Task<ValidationResult> ValidateAsync(
            ValidationContext<TestRequest> context,
            CancellationToken cancellation = default) => Task.FromResult(
            new ValidationResult(
            [
                new ValidationFailure(nameof(TestRequest.Value), "Required"),
                new ValidationFailure(nameof(TestRequest.Value), "Required")
            ]));
    }
}
