using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.AiReview;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanAiReviewServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly List<PlanAiReview> stored = [];
    private readonly Mock<IGeminiTextGenerationProvider> generator = new();
    private readonly Mock<IPlanAiReviewQueue> queue = new();
    private bool busy;

    [TestInitialize]
    public void ConfigureFakes()
    {
        generator.SetupGet(g => g.IsConfigured).Returns(true);
        // A one-slot queue, like the real channel: a request makes it busy until the review is written.
        queue.SetupGet(q => q.IsBusy).Returns(() => busy);
        queue.Setup(q => q.TryRequest(It.IsAny<Guid>())).Returns(() => !busy && (busy = true));
    }

    // One plan, too small to score, with nothing to compare churn on: the smallest valid answer.
    private const string ValidAnswer = """
        {"plans":[{"plan":"starter","status":"not_enough_data","score":null,"score_reason":"Too few subscribers to score.",
        "score_metrics":[],"summary":"Starter has too little data.","summary_metrics":[],"outlook":{"direction":"stable","reason":"Too few to tell.",
        "metrics":["subscribers_now"]},"strengths":[],"risks":[],"feature_insights":[],"churn_reasons":[],
        "actions":[]}],"overall":"Trial conversion is unknown.","overall_metrics":[]}
        """;

    [TestMethod]
    public async Task GenerateAsync_WhenTheModelTimesOut_StoresAFailedAttemptAndNoReview()
    {
        generator.Setup(g => g.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timed out", null, HttpStatusCode.GatewayTimeout));

        await Service().GenerateAsync(null);

        var review = stored.Single();
        review.Status.Should().Be("failed");
        review.ReviewJson.Should().BeNull();
        review.ErrorMessage.Should().Be("The AI model did not answer in time.");
        review.InputJson.Should().Contain("\"plans\"");
    }

    [TestMethod]
    public async Task GenerateAsync_WhenTheModelAnswersEmpty_TriesAgainThenStoresAFailedAttempt()
    {
        generator.Setup(g => g.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Gemini returned an empty generation."));

        await Service().GenerateAsync(null);

        generator.Verify(g => g.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        stored.Single().Status.Should().Be("failed");
        stored.Single().ErrorMessage.Should().Be("The AI model returned an empty answer.");
    }

    [TestMethod]
    public async Task GenerateAsync_WhenTheFirstAnswerFailsValidation_StoresOnlyTheValidSecondOne()
    {
        // An unknown plan breaks the structure, the one thing a retry is for.
        var brokenAnswer = ValidAnswer.Replace("\"plan\":\"starter\"", "\"plan\":\"pro\"");
        generator.SetupSequence(g => g.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiTextGenerationResult(brokenAnswer, "gemini-3.5-flash", 100, 200, null))
            .ReturnsAsync(new GeminiTextGenerationResult(ValidAnswer, "gemini-3.5-flash", 100, 200, null));

        await Service().GenerateAsync(Guid.NewGuid());

        var review = stored.Single();
        review.Status.Should().Be("succeeded");
        review.ErrorMessage.Should().BeNull();
        review.RawAnswer.Should().Be(ValidAnswer, "the model's own words are kept");
        JsonSerializer.Deserialize<PlanAiReviewOutput>(review.ReviewJson!, PlanAiReviewInputBuilder.Json)!.Plans.Single().Score.Should().BeNull();
    }

    [TestMethod]
    public async Task GenerateAsync_AfterARejectedAnswer_AsksAgainWithWhatWasWrong()
    {
        var prompts = new List<string>();
        // An unknown plan breaks the structure, the one thing a retry is for.
        var brokenAnswer = ValidAnswer.Replace("\"plan\":\"starter\"", "\"plan\":\"pro\"");
        generator.SetupSequence(g => g.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiTextGenerationResult(brokenAnswer, "gemini-3.5-flash", 100, 200, null))
            .ReturnsAsync(new GeminiTextGenerationResult(ValidAnswer, "gemini-3.5-flash", 100, 200, null));
        generator.Setup(g => g.IsConfigured).Returns(true);
        generator.Invocations.Clear();

        await Service().GenerateAsync(null);

        prompts.AddRange(generator.Invocations
            .Where(call => call.Method.Name == nameof(IGeminiTextGenerationProvider.GenerateAsync))
            .Select(call => ((GeminiTextGenerationRequest)call.Arguments[0]).Prompt));
        prompts.Should().HaveCount(2);
        prompts[0].Should().NotContain("rejected by the checker");
        prompts[1].Should().StartWith(prompts[0]).And.Contain("rejected by the checker")
            .And.Contain("Plan starter is not reviewed.");
    }

    [TestMethod]
    public async Task GenerateAsync_WhenTheRetryHitsAnOutage_KeepsWhyTheFirstAnswerWasRejected()
    {
        // An unknown plan breaks the structure, the one thing a retry is for.
        var brokenAnswer = ValidAnswer.Replace("\"plan\":\"starter\"", "\"plan\":\"pro\"");
        generator.SetupSequence(g => g.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiTextGenerationResult(brokenAnswer, "gemini-3.5-flash", 100, 200, null))
            .ThrowsAsync(new HttpRequestException("overloaded", null, HttpStatusCode.ServiceUnavailable));

        await Service().GenerateAsync(null);

        var review = stored.Single();
        review.Status.Should().Be("failed");
        review.ErrorMessage.Should().StartWith("The AI answer failed validation:").And.EndWith("Retry: The AI model is overloaded.");
    }

    [TestMethod]
    public async Task GenerateAsync_SendsTheSchemaModelAndLongTimeout()
    {
        GeminiTextGenerationRequest? sent = null;
        generator.Setup(g => g.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GeminiTextGenerationRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new GeminiTextGenerationResult(ValidAnswer, "gemini-3.5-flash", 100, 200, null));

        await Service().GenerateAsync(null);

        sent!.ResponseSchema.Should().NotBeNull();
        sent.Model.Should().Be("gemini-3.5-flash");
        sent.AttemptTimeout.Should().Be(TimeSpan.FromSeconds(300));
        sent.SystemInstruction.Should().Be(PlanAiReviewPrompt.System);
    }

    [TestMethod]
    public async Task IsDueAsync_FollowsTheRefreshIntervalAndTheRetryDelay()
    {
        (await Service().IsDueAsync()).Should().BeTrue("there is no review yet");

        stored.Add(Review("succeeded", Now.UtcDateTime.AddDays(-13)));
        (await Service().IsDueAsync()).Should().BeFalse("the review is 13 days old");

        stored.Add(Review("succeeded", Now.UtcDateTime.AddDays(-14)));
        stored.RemoveAt(0);
        (await Service().IsDueAsync()).Should().BeTrue("the review is 14 days old");

        stored.Add(Review("failed", Now.UtcDateTime.AddHours(-2)));
        (await Service().IsDueAsync()).Should().BeFalse("an attempt failed 2 hours ago");
    }

    [TestMethod]
    public async Task IsDueAsync_WithoutAGeminiKey_IsNeverDue()
    {
        generator.SetupGet(g => g.IsConfigured).Returns(false);

        (await Service().IsDueAsync()).Should().BeFalse();
    }

    [TestMethod]
    public async Task RequestAsync_ByDefault_CanBeAskedAgainRightAfterAReview()
    {
        stored.Add(Review("failed", Now.UtcDateTime.AddMinutes(-1), triggeredBy: Guid.NewGuid()));

        var result = await Service().RequestAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.RegenerateAvailableAt.Should().BeNull();
    }

    [TestMethod]
    public async Task RequestAsync_WithinTheCooldown_IsRefused()
    {
        stored.Add(Review("succeeded", Now.UtcDateTime.AddHours(-3), triggeredBy: Guid.NewGuid()));

        var result = await Service(new PlanAiReviewOptions { ManualRegenerateCooldownHours = 24 }).RequestAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.PlanAiReviewCoolingDown);
    }

    [TestMethod]
    public async Task RequestAsync_WhileAReviewIsBeingWritten_IsRefused()
    {
        busy = true;

        var result = await Service().RequestAsync();

        result.Error.Code.Should().Be(ErrorCodes.PlanAiReviewInProgress);
    }

    [TestMethod]
    public async Task RequestAsync_OutsideTheCooldown_QueuesAReview()
    {
        var result = await Service().RequestAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.IsGenerating.Should().BeTrue();
        queue.Verify(q => q.TryRequest(It.IsAny<Guid>()), Times.Once);
        queue.Object.TryRequest(Guid.NewGuid()).Should().BeFalse("one review is already queued");
    }

    [TestMethod]
    public async Task GetAsync_GivesTheMrrBridgesTheModelWasGiven()
    {
        var review = Review("succeeded", Now.UtcDateTime.AddDays(-1));
        review.InputJson = JsonSerializer.Serialize(PlanAiReviewFixtures.Input(), PlanAiReviewInputBuilder.Json);
        review.ReviewJson = JsonSerializer.Serialize(PlanAiReviewFixtures.ValidReview(), PlanAiReviewInputBuilder.Json);
        stored.Add(review);

        var bridges = (await Service().GetAsync()).Value.Latest!.MrrBridges;

        var all = bridges[0];
        all.PlanName.Should().BeNull();
        (all.Start, all.New, all.Expansion, all.Contraction, all.Churn, all.End)
            .Should().Be((142_286_354m, 61_187_855m, 1_000_000m, 0m, 38_484_250m, 165_989_959m), "the net of moves between plans is shown as expansion");
        var starter = bridges[1];
        starter.PlanName.Should().Be("Starter");
        starter.End.Should().Be(starter.Start + starter.New + starter.MovedIn + starter.Expansion - starter.Contraction - starter.MovedOut - starter.Churn);
        bridges.Should().HaveCount(4, "all plans, then each of the three plans");
    }

    private PlanAiReviewService Service(PlanAiReviewOptions? options = null)
    {
        var reviews = new Mock<IRepository<PlanAiReview>>();
        reviews.Setup(r => r.Query()).Returns(() => stored.AsQueryable().BuildMock());
        reviews.Setup(r => r.AddAsync(It.IsAny<PlanAiReview>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<PlanAiReview, bool, CancellationToken>((review, _, _) => stored.Add(review))
            .Returns(Task.CompletedTask);

        var insights = new Mock<IPlanInsightsService>();
        insights.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Insights()));
        var revenue = new Mock<IRevenueService>();
        revenue.Setup(s => s.GetChartsAsync(It.IsAny<RevenueFilterDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new RevenueChartDto()));
        revenue.Setup(s => s.GetInsightsAsync(It.IsAny<RevenueFilterDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new RevenueInsightsDto()));
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.UserId).Returns(Guid.NewGuid());

        return new PlanAiReviewService(
            reviews.Object,
            insights.Object,
            revenue.Object,
            generator.Object,
            queue.Object,
            currentUser.Object,
            Options.Create(options ?? new PlanAiReviewOptions()),
            new FakeTimeProvider(Now),
            NullLogger<PlanAiReviewService>.Instance);
    }

    // A Starter plan with 3 subscribers, below Plan Insights' minimum sample, and no churn comparison.
    private static PlanInsightsDto Insights() => new(
        90,
        Now.UtcDateTime,
        20,
        new TrialInsightDto(0, 0, 0, 0, 0, 0, null, null, []),
        [new PlanGroupInsightDto("starter", "Starter", false, 3, false, 249_750m,
            [new FeatureAdoptionDto("design_images", "Design images", true, 2, 66.7m)], new PlanCostDto(0m, 249_750m, 0m))],
        [],
        [],
        [],
        new VerdictRulesDto(1.5m, 60m, 30m),
        []);

    private static PlanAiReview Review(string status, DateTime at, Guid? triggeredBy = null) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        GeneratedAt = at,
        InputJson = "{}",
        ReviewJson = status == "succeeded" ? "{\"plans\":[],\"overall\":\"x\"}" : null,
        TriggeredBy = triggeredBy
    };
}
