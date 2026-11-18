using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Onboarding.Common;
using APCS.Application.Features.Onboarding.Dtos;
using APCS.Application.Features.Onboarding.Dtos.Request;
using APCS.Application.Features.Onboarding.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Onboarding;

/// <summary>
/// Reads and stores the authenticated user's onboarding state.
/// </summary>
public sealed class OnboardingProgressService(
    IOnboardingProgressRepository repository,
    ICurrentUser currentUser)
    : IOnboardingProgressService
{
    public async Task<Result<OnboardingProgressResponse>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Result.Failure<OnboardingProgressResponse>(OnboardingErrors.Unauthenticated());
        }

        var state = await repository.GetAsync(currentUser.UserId.Value, cancellationToken)
            ?? OnboardingProgressRules.EmptyState;
        var signals = await repository.GetSignalsAsync(currentUser.UserId.Value, cancellationToken);
        return Result.Success(Map(state, signals));
    }

    public async Task<Result<OnboardingProgressResponse>> UpdateAsync(
        UpdateOnboardingProgressRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!OnboardingProgressRules.TryValidate(request, out var errorDetails))
        {
            return Result.Failure<OnboardingProgressResponse>(Error.Validation(
                "Onboarding progress contains invalid entries.",
                errorDetails));
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Result.Failure<OnboardingProgressResponse>(OnboardingErrors.Unauthenticated());
        }

        var state = new OnboardingProgressState(
            request.GuideProgress.ToDictionary(
                entry => entry.Key,
                entry => new GuideProgress(entry.Value.Version, entry.Value.Status, entry.Value.Step),
                StringComparer.Ordinal),
            request.ChecklistDismissed);

        var saved = await repository.SaveAsync(currentUser.UserId.Value, state, cancellationToken);
        if (!saved)
        {
            return Result.Failure<OnboardingProgressResponse>(OnboardingErrors.ProgressUnavailable());
        }

        var signals = await repository.GetSignalsAsync(currentUser.UserId.Value, cancellationToken);
        return Result.Success(Map(state, signals));
    }

    private static OnboardingProgressResponse Map(OnboardingProgressState state, OnboardingSignals signals) =>
        new(
            state.GuideProgress.ToDictionary(
                entry => entry.Key,
                entry => new GuideProgressDto(entry.Value.Version, entry.Value.Status, entry.Value.Step),
                StringComparer.Ordinal),
            state.ChecklistDismissed,
            new OnboardingSignalsResponse(
                signals.ApiKeyConnected,
                signals.BatchCreated,
                signals.MockupTemplateUploaded,
                signals.WorkflowRunStarted));
}
