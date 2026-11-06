using System.Net;
using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans.Common;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Admin.SubscriptionPlans.AiReview;

/// <inheritdoc />
/// <remarks>
/// The model only writes the commentary. Every figure comes from Plan Insights and the Revenue page
/// for the same days, including whether a plan or its churn has enough data, and every answer is
/// checked by <see cref="PlanAiReviewValidator"/> before it is stored.
/// </remarks>
public sealed class PlanAiReviewService(
    IRepository<PlanAiReview> reviews,
    IPlanInsightsService insightsService,
    IRevenueService revenueService,
    IGeminiTextGenerationProvider generator,
    IPlanAiReviewQueue queue,
    ICurrentUser currentUser,
    IOptions<PlanAiReviewOptions> options,
    TimeProvider timeProvider,
    ILogger<PlanAiReviewService> logger) : IPlanAiReviewService
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    private PlanAiReviewOptions Settings => options.Value;

    /// <inheritdoc />
    public async Task<Result<PlanAiReviewDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var successes = await reviews.Query()
            .Where(review => review.Status == Succeeded)
            .OrderByDescending(review => review.GeneratedAt)
            .Take(2)
            .ToListAsync(cancellationToken);
        var latest = successes.FirstOrDefault();

        var lastFailureQuery = reviews.Query().Where(review => review.Status == Failed);
        if (latest is not null)
        {
            var latestUtc = DateTime.SpecifyKind(latest.GeneratedAt, DateTimeKind.Utc);
            lastFailureQuery = lastFailureQuery.Where(review => review.GeneratedAt > latestUtc);
        }

        var lastFailure = await lastFailureQuery
            .OrderByDescending(review => review.GeneratedAt)
            .Select(review => (DateTime?)review.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var lastRequest = await LastRequestAsync(cancellationToken);

        var regenerateAt = lastRequest?.AddHours(Settings.ManualRegenerateCooldownHours);

        PlanAiReviewSnapshotDto? snapshot = null;
        if (latest is not null)
        {
            try
            {
                snapshot = Snapshot(latest);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize plan AI review snapshot {ReviewId}.", latest.Id);
            }
        }

        IReadOnlyList<PlanAiScoreDto> previousScores = [];
        if (successes.Count > 1)
        {
            try
            {
                previousScores = Scores(successes[1]);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize previous plan AI review score {ReviewId}.", successes[1].Id);
            }
        }

        return Result.Success(new PlanAiReviewDto(
            generator.IsConfigured,
            queue.IsBusy,
            snapshot,
            previousScores,
            latest?.GeneratedAt.AddDays(Settings.RefreshIntervalDays),
            lastFailure,
            regenerateAt > Now ? regenerateAt : null));
    }

    /// <inheritdoc />
    public async Task<Result<PlanAiReviewDto>> RequestAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } adminId)
            return Result.Failure<PlanAiReviewDto>(PlanAiReviewErrors.Unauthenticated());
        if (!generator.IsConfigured)
            return Result.Failure<PlanAiReviewDto>(PlanAiReviewErrors.NotConfigured());
        if (queue.IsBusy)
            return Result.Failure<PlanAiReviewDto>(PlanAiReviewErrors.InProgress());

        var lastRequest = await LastRequestAsync(cancellationToken);
        if (lastRequest?.AddHours(Settings.ManualRegenerateCooldownHours) is { } availableAt && availableAt > Now)
            return Result.Failure<PlanAiReviewDto>(PlanAiReviewErrors.CoolingDown(availableAt));

        if (!queue.TryRequest(adminId))
            return Result.Failure<PlanAiReviewDto>(PlanAiReviewErrors.InProgress());

        return await GetAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsDueAsync(CancellationToken cancellationToken = default)
    {
        if (!Settings.Enabled || !generator.IsConfigured) return false;

        var latest = await reviews.Query()
            .OrderByDescending(review => review.GeneratedAt)
            .Select(review => new { review.Status, review.GeneratedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null) return true;

        return latest.Status == Succeeded
            ? latest.GeneratedAt.AddDays(Settings.RefreshIntervalDays) <= Now
            : latest.GeneratedAt.AddHours(Settings.RetryAfterFailureHours) <= Now;
    }

    /// <inheritdoc />
    public async Task GenerateAsync(Guid? triggeredBy, CancellationToken cancellationToken = default)
    {
        var to = DateOnly.FromDateTime(Now);
        // The same days Plan Insights shows, so the review and the page always describe one period.
        var from = to.AddDays(1 - PlanInsightsService.PeriodDays);
        var record = new PlanAiReview
        {
            Id = Guid.NewGuid(),
            Status = Failed,
            PeriodStart = from,
            PeriodEnd = to,
            InputJson = "{}",
            TriggeredBy = triggeredBy,
        };

        try
        {
            var input = await BuildInputAsync(from, to, cancellationToken);
            if (input is null)
            {
                record.ErrorMessage = "The figures for the review could not be loaded.";
            }
            else
            {
                record.InputJson = PlanAiReviewInputBuilder.Serialize(input);
                await WriteAsync(input, record, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The plan AI review could not be written.");
            record.ErrorMessage = "The review could not be written.";
        }

        record.GeneratedAt = Now;
        await reviews.AddAsync(record, saveChange: true, cancellationToken);
    }

    // Asks the model up to MaxGenerations times. Only an answer whose structure is broken is retried;
    // any other part that breaks a rule is left out or rewritten by the validator.
    private async Task WriteAsync(PlanAiReviewInput input, PlanAiReview record, CancellationToken cancellationToken)
    {
        var request = new GeminiTextGenerationRequest(
            record.InputJson,
            PlanAiReviewPrompt.System,
            PlanAiReviewPrompt.Schema(),
            Settings.Model,
            Settings.FallbackModel,
            TimeSpan.FromSeconds(Settings.AttemptTimeoutSeconds));

        for (var generation = 1; generation <= Settings.MaxGenerations; generation++)
        {
            GeminiTextGenerationResult result;
            try
            {
                result = await generator.GenerateAsync(request, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                // Only the status is kept: the provider's own message is not shown to Admins.
                logger.LogWarning(exception, "Gemini failed while writing the plan AI review.");
                var failure = exception.StatusCode switch
                {
                    HttpStatusCode.GatewayTimeout => "The AI model did not answer in time.",
                    HttpStatusCode.TooManyRequests => "The AI model's quota is used up.",
                    HttpStatusCode.ServiceUnavailable => "The AI model is overloaded.",
                    _ => "The AI model could not be reached."
                };
                // Keep why an earlier answer was rejected: it says more than the later outage.
                record.ErrorMessage = record.ErrorMessage is null ? failure : $"{record.ErrorMessage} Retry: {failure}";
                return;
            }
            catch (InvalidOperationException exception)
            {
                logger.LogWarning(exception, "Gemini returned no plan AI review.");
                record.ErrorMessage = "The AI model returned an empty answer.";
                continue;
            }

            record.Model = result.Model;
            record.InputTokens = result.InputTokens;
            record.OutputTokens = result.OutputTokens;
            // The model's own words, kept so the prompt can be fixed where it goes wrong.
            record.RawAnswer = result.Text;

            var validation = PlanAiReviewValidator.Validate(result.Text, input);
            if (validation.IsValid)
            {
                record.Status = Succeeded;
                record.ReviewJson = JsonSerializer.Serialize(validation.Review, PlanAiReviewInputBuilder.Json);
                record.ErrorMessage = null;
                record.DroppedParts = validation.Dropped.Count == 0 ? null : string.Join("\n", validation.Dropped);
                if (validation.Dropped.Count > 0)
                    logger.LogInformation("The plan AI review was stored without {Count} parts: {Dropped}", validation.Dropped.Count,
                        string.Join(" | ", validation.Dropped.Take(10)));
                return;
            }

            logger.LogWarning("The plan AI review failed validation: {Errors}", string.Join(" | ", validation.Errors.Take(10)));
            record.ErrorMessage = "The AI answer failed validation: " + string.Join(" ", validation.Errors.Take(5));
            // The next try sees what was wrong, so it fixes that instead of starting over.
            request = request with { Prompt = PlanAiReviewPrompt.Repair(record.InputJson, validation.Errors) };
        }
    }

    // The same days and figures the Plan Insights and Revenue pages show for the period.
    private async Task<PlanAiReviewInput?> BuildInputAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var insights = await insightsService.GetAsync(cancellationToken);
        var filter = new RevenueFilterDto { StartDate = from, EndDate = to, Granularity = "Month" };
        var charts = await revenueService.GetChartsAsync(filter, cancellationToken);
        var revenue = await revenueService.GetInsightsAsync(filter, cancellationToken);
        if (!insights.IsSuccess || !charts.IsSuccess || !revenue.IsSuccess) return null;

        return PlanAiReviewInputBuilder.Build(
            insights.Value,
            charts.Value.ByPlan,
            revenue.Value.MrrMovement.Period,
            from,
            to);
    }

    private Task<DateTime?> LastRequestAsync(CancellationToken cancellationToken) =>
        reviews.Query()
            .Where(review => review.TriggeredBy != null)
            .OrderByDescending(review => review.GeneratedAt)
            .Select(review => (DateTime?)review.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static PlanAiReviewSnapshotDto Snapshot(PlanAiReview review)
    {
        if (string.IsNullOrWhiteSpace(review.InputJson))
            throw new InvalidOperationException("InputJson is empty.");
        if (string.IsNullOrWhiteSpace(review.ReviewJson))
            throw new InvalidOperationException("ReviewJson is empty.");

        var input = JsonSerializer.Deserialize<PlanAiReviewInput>(review.InputJson, PlanAiReviewInputBuilder.Json)
            ?? throw new InvalidOperationException("InputJson could not be deserialized.");
        var output = JsonSerializer.Deserialize<PlanAiReviewOutput>(review.ReviewJson, PlanAiReviewInputBuilder.Json)
            ?? throw new InvalidOperationException("ReviewJson could not be deserialized.");

        return new PlanAiReviewSnapshotDto(
            review.Id,
            review.GeneratedAt,
            review.PeriodStart,
            review.PeriodEnd,
            review.Model,
            (input.Plans ?? [])
                .Select(plan => new PlanAiReviewPlanNameDto(
                    plan.Key,
                    plan.Name,
                    (plan.Features ?? []).Select(feature => new PlanAiReviewFeatureNameDto(feature.Key, feature.Name)).ToList()))
                .ToList(),
            output,
            MrrBridges(input));
    }

    // All plans together only have the net of moves between plans, shown as expansion or contraction.
    private static IReadOnlyList<MrrBridgeDto> MrrBridges(PlanAiReviewInput input)
    {
        var totals = input.Totals;
        var planChanges = totals?.MrrPlanChangesVnd ?? 0m;
        var all = new MrrBridgeDto
        {
            Start = totals?.MrrStartVnd ?? 0m,
            New = totals?.MrrNewVnd ?? 0m,
            Expansion = Math.Max(planChanges, 0m),
            Contraction = Math.Max(-planChanges, 0m),
            Churn = totals?.MrrChurnVnd ?? 0m,
            End = totals?.MrrEndVnd ?? 0m
        };
        return (input.Plans ?? [])
            .Where(plan => plan.Metrics?.MrrStartVnd is not null || plan.Metrics?.MrrEndVnd is not null)
            .Select(plan => new MrrBridgeDto
            {
                PlanName = plan.Name,
                Start = plan.Metrics?.MrrStartVnd ?? 0m,
                New = plan.Metrics?.MrrNewVnd ?? 0m,
                MovedIn = plan.Metrics?.MrrMovedInVnd ?? 0m,
                Expansion = plan.Metrics?.MrrExpansionVnd ?? 0m,
                Contraction = plan.Metrics?.MrrContractionVnd ?? 0m,
                MovedOut = plan.Metrics?.MrrMovedOutVnd ?? 0m,
                Churn = plan.Metrics?.MrrChurnVnd ?? 0m,
                End = plan.Metrics?.MrrEndVnd ?? 0m
            })
            .Prepend(all)
            .ToList();
    }

    private static IReadOnlyList<PlanAiScoreDto> Scores(PlanAiReview review)
    {
        if (string.IsNullOrWhiteSpace(review.ReviewJson)) return [];
        var output = JsonSerializer.Deserialize<PlanAiReviewOutput>(review.ReviewJson, PlanAiReviewInputBuilder.Json);
        return output?.Plans?
            .Select(plan => new PlanAiScoreDto(plan.Plan, plan.Status, plan.Score))
            .ToList() ?? [];
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
}
