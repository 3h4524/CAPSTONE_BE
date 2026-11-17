using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Onboarding;
using APCS.Application.Features.Onboarding.Dtos;
using APCS.Application.Features.Onboarding.Dtos.Request;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Onboarding;

[TestClass]
public sealed class OnboardingProgressServiceTests
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly OnboardingSignals Signals = new(true, false, true, false);

    private static readonly OnboardingProgressState SavedState = new(
        new Dictionary<string, GuideProgress>(StringComparer.Ordinal)
        {
            ["api-keys"] = new(1, OnboardingGuideStatuses.Active, 2)
        },
        false);

    [TestMethod]
    public async Task GetAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var repository = new Mock<IOnboardingProgressRepository>(MockBehavior.Strict);
        var service = CreateService(repository, authenticated: false);

        var result = await service.GetAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task GetAsync_WhenNothingSaved_ReturnsEmptyProgressWithSignals()
    {
        var repository = new Mock<IOnboardingProgressRepository>();
        repository.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OnboardingProgressState?)null);
        repository.Setup(r => r.GetSignalsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Signals);
        var service = CreateService(repository);

        var result = await service.GetAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.GuideProgress.Should().BeEmpty();
        result.Value.ChecklistDismissed.Should().BeFalse();
        result.Value.Signals.Should().Be(new Application.Features.Onboarding.Dtos.Response.OnboardingSignalsResponse(
            Signals.ApiKeyConnected,
            Signals.BatchCreated,
            Signals.MockupTemplateUploaded,
            Signals.WorkflowRunStarted));
    }

    [TestMethod]
    public async Task GetAsync_WhenProgressSaved_ReturnsStoredGuidesAndDismissal()
    {
        var repository = new Mock<IOnboardingProgressRepository>();
        repository.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SavedState with { ChecklistDismissed = true });
        repository.Setup(r => r.GetSignalsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Signals);
        var service = CreateService(repository);

        var result = await service.GetAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ChecklistDismissed.Should().BeTrue();
        result.Value.GuideProgress.Should().ContainKey("api-keys").WhoseValue.Should().Be(
            new GuideProgressDto(1, OnboardingGuideStatuses.Active, 2));
    }

    [TestMethod]
    public async Task UpdateAsync_WhenKeyIsNotASlug_ReturnsValidationError()
    {
        var repository = new Mock<IOnboardingProgressRepository>(MockBehavior.Strict);
        var service = CreateService(repository);

        var result = await service.UpdateAsync(
            new UpdateOnboardingProgressRequest(
                new Dictionary<string, GuideProgressDto>
                {
                    ["API Keys"] = new(1, OnboardingGuideStatuses.Completed, 0)
                },
                false),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Validation);
        result.Error.Details.Should().ContainKey("GuideProgress.API Keys");
        repository.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateAsync_WhenStatusIsUnknown_ReturnsValidationError()
    {
        var repository = new Mock<IOnboardingProgressRepository>(MockBehavior.Strict);
        var service = CreateService(repository);

        var result = await service.UpdateAsync(
            new UpdateOnboardingProgressRequest(
                new Dictionary<string, GuideProgressDto>
                {
                    ["batches"] = new(1, "finished", 0)
                },
                false),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Validation);
        result.Error.Details.Should().ContainKey("GuideProgress.batches");
    }

    [TestMethod]
    public async Task UpdateAsync_WhenVersionOrStepIsOutOfRange_ReturnsValidationError()
    {
        var repository = new Mock<IOnboardingProgressRepository>(MockBehavior.Strict);
        var service = CreateService(repository);

        var result = await service.UpdateAsync(
            new UpdateOnboardingProgressRequest(
                new Dictionary<string, GuideProgressDto>
                {
                    ["mockups"] = new(0, OnboardingGuideStatuses.Active, 0)
                },
                false),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenTooManyGuides_ReturnsValidationError()
    {
        var repository = new Mock<IOnboardingProgressRepository>(MockBehavior.Strict);
        var service = CreateService(repository);
        var guides = Enumerable.Range(0, 21).ToDictionary(
            index => $"guide-{index}",
            _ => new GuideProgressDto(1, OnboardingGuideStatuses.Completed, 0),
            StringComparer.Ordinal);

        var result = await service.UpdateAsync(
            new UpdateOnboardingProgressRequest(guides, false),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenRepositoryCannotSave_ReturnsUnavailable()
    {
        var repository = new Mock<IOnboardingProgressRepository>();
        repository.Setup(r => r.SaveAsync(UserId, It.IsAny<OnboardingProgressState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(repository);

        var result = await service.UpdateAsync(ValidRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("onboarding.unavailable");
    }

    [TestMethod]
    public async Task UpdateAsync_WhenValid_SavesEveryFieldAndReturnsSignals()
    {
        OnboardingProgressState? saved = null;
        var repository = new Mock<IOnboardingProgressRepository>();
        repository.Setup(r => r.SaveAsync(UserId, It.IsAny<OnboardingProgressState>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, OnboardingProgressState, CancellationToken>((_, state, _) => saved = state)
            .ReturnsAsync(true);
        repository.Setup(r => r.GetSignalsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Signals);
        var service = CreateService(repository);

        var result = await service.UpdateAsync(ValidRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.ChecklistDismissed.Should().BeTrue();
        saved.GuideProgress.Should().ContainKey("workflows").WhoseValue.Should().Be(
            new GuideProgress(2, OnboardingGuideStatuses.Completed, 3));
        result.Value.GuideProgress["workflows"].Should().Be(
            new GuideProgressDto(2, OnboardingGuideStatuses.Completed, 3));
        result.Value.Signals.WorkflowRunStarted.Should().Be(Signals.WorkflowRunStarted);
    }

    private static UpdateOnboardingProgressRequest ValidRequest() =>
        new(
            new Dictionary<string, GuideProgressDto>
            {
                ["workflows"] = new(2, OnboardingGuideStatuses.Completed, 3)
            },
            true);

    private static OnboardingProgressService CreateService(
        Mock<IOnboardingProgressRepository> repository,
        bool authenticated = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(user => user.IsAuthenticated).Returns(authenticated);
        currentUser.Setup(user => user.UserId).Returns(UserId);
        return new OnboardingProgressService(repository.Object, currentUser.Object);
    }
}
