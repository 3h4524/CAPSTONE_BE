using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans.Common;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.DesignGeneration.Common;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.UsageStatistics;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <inheritdoc />
/// <remarks>
/// Usage is counted from the records each feature leaves behind (design images, mockups, videos,
/// listings, exports), not from usage counters, so every feature is measured the same way. A
/// Seller is grouped by the plan in effect now, which is close enough over 45 days.
/// </remarks>
public sealed class PlanInsightsService(
    IRepository<Subscription> subscriptions,
    IRepository<Invoice> invoices,
    IRepository<Product> products,
    IRepository<DesignImage> designImages,
    IRepository<MockupImage> mockups,
    IRepository<PromoVideo> videos,
    IRepository<ApiUsageRecord> usageRecords,
    IRepository<ExportPackage> exports,
    IRepository<WorkflowRun> runs,
    IRepository<BatchJob> batchJobs,
    IRepository<MockupTemplate> mockupTemplates,
    IRepository<ListingGenerationHistory> listingHistory,
    IRepository<AiPrompt> prompts,
    IRepository<Domain.Entities.ListingContent> listings,
    IRepository<EtsyUploadLog> etsyUploads,
    IRepository<PrintifyUploadLog> printifyUploads,
    WorkflowCapabilityRegistry workflowCapabilities,
    TimeProvider timeProvider,
    IOptions<PaymentGatewaySettings> gatewaySettings,
    IRevenueService revenueService,
    IOptions<PlanVerdictOptions> verdictOptions) : IPlanInsightsService
{
    /// <summary>
    /// Estimated cost of one mockup composite on the platform's Cloudinary account. A placeholder
    /// until the team's Cloudinary price per transformation is confirmed.
    /// </summary>
    internal const decimal MockupRenderUsd = 0.002m;

    /// <summary>Estimated media-worker cost of one standard video render. A placeholder.</summary>
    internal const decimal VideoRenderUsd = 0.01m;

    /// <summary>
    /// The days Plan Insights and the AI review cover: recent enough to show what changed, long
    /// enough for a plan to have customers who left.
    /// </summary>
    public const int PeriodDays = 45;

    private const string TrialKey = "trial";

    private sealed record Metric(string Key, string Label, string? PlanFeature, bool CostsPlatform);

    private sealed record UsageEvent(Guid UserId, DateTime CreatedAt, decimal CostUsd);

    private sealed record Group(string Key, string Name, bool IsTrial, bool OnSale, decimal MonthlyPriceUsd,
        IReadOnlyDictionary<string, PlanFeatureValue> Features, HashSet<Guid> UserIds);

    private static readonly Metric[] Metrics =
    [
        new("design_images", "Design images", null, false),
        new("mockups", "Rendered mockups", null, true),
        new("uploaded_mockups", "Uploaded mockups", null, false),
        new("lifestyle_mockups", "Lifestyle mockups (AI)", PlanFeatures.LifestyleMockup, false),
        new("listings", "Listings", null, true),
        new("standard_videos", "Standard videos", null, true),
        new("ai_videos", "AI videos", PlanFeatures.AiVideo, false),
        new("studio_keyframes", "AI studio keyframes", PlanFeatures.StudioKeyframe, false),
        new("exports", "ZIP exports", null, false),
    ];

    // What only Pro unlocks beyond the features above, for what trial users tried: a second design
    // variation, skipping design review, their own mockup template, a regenerated listing and a
    // landscape video.
    private static readonly Metric[] ProTrialMetrics =
    [
        new("design_variations", "Design variations (2+ per product)", PlanFeatures.DesignVariations, false),
        new("auto_approval", "Automatic design approval", PlanFeatures.AutoApproval, false),
        new("custom_mockup_templates", "Custom mockup templates", PlanFeatures.CustomMockupTemplates, false),
        new("listing_regenerations", "Listing regeneration", PlanFeatures.ListingRegenerate, false),
        new("landscape_videos", "Landscape videos", PlanFeatures.AllVideoFormats, false),
    ];

    // The daily safety caps and the usage each one counts.
    private static readonly (string Code, string Metric)[] Caps =
    [
        (PlanFeatures.DailyMockupCap, "mockups"),
        (PlanFeatures.DailyListingCap, "listings"),
        (PlanFeatures.DailyVideoCap, "standard_videos"),
    ];

    /// <inheritdoc />
    public async Task<Result<PlanInsightsDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        const int days = PeriodDays;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var from = now.AddDays(-days);

        // Churn signals look back before the period; everything else counts from its start.
        var history = from.AddDays(-PlanChurnSignals.HistoryDays);
        var allEvents = new Dictionary<string, List<UsageEvent>>();
        var events = new Dictionary<string, List<UsageEvent>>();
        foreach (var metric in Metrics)
        {
            allEvents[metric.Key] = await LoadEventsAsync(metric.Key, history, now, cancellationToken);
            events[metric.Key] = allEvents[metric.Key].Where(item => item.CreatedAt >= from).ToList();
        }

        var groups = await LoadGroupsAsync(now, cancellationToken);

        var planInsights = groups
            .Select(group => BuildGroup(group, events, days))
            .ToList();

        // Trials are compared on every Pro feature, including the ones only Pro unlocks.
        var trialEvents = new Dictionary<string, List<UsageEvent>>(events);
        foreach (var metric in ProTrialMetrics)
            trialEvents[metric.Key] = await LoadEventsAsync(metric.Key, from, now, cancellationToken);
        var trial = await BuildTrialAsync(from, now, days, trialEvents, cancellationToken);
        var pipeline = await BuildPipelineAsync(from, now, cancellationToken);
        var verdicts = await BuildVerdictsAsync(groups, planInsights, events, from, now, cancellationToken);
        var churnSignals = await BuildChurnSignalsAsync(
            groups, allEvents, DateOnly.FromDateTime(from).AddDays(1), DateOnly.FromDateTime(now), cancellationToken);

        return Result.Success(new PlanInsightsDto(
            days,
            now,
            Rules.MinimumSample,
            trial,
            planInsights,
            pipeline,
            [
                new("mockup_render", "Mockup render (Cloudinary)", Vnd(MockupRenderUsd), false),
                new("video_render", "Standard video render (worker)", Vnd(VideoRenderUsd), false),
                new("listing", "Listing generation (platform Gemini key)", 0m, true),
            ],
            verdicts,
            new VerdictRulesDto(Rules.PremiumPriceRatio, Rules.LowRetentionRate, Rules.PromoteBelowRate),
            churnSignals));
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>?> UsersOfFeatureAsync(string featureCode, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        // Listing content has no metric of its own: every generated listing uses it.
        var metric = featureCode == PlanFeatures.WorkflowListing
            ? "listings"
            : Metrics.Concat(ProTrialMetrics).FirstOrDefault(candidate => candidate.PlanFeature == featureCode)?.Key;
        if (metric is null) return null;

        var events = await LoadEventsAsync(metric, from, to, cancellationToken);
        return events.Select(usage => usage.UserId).ToHashSet();
    }

    private async Task<List<UsageEvent>> LoadEventsAsync(string metric, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        metric switch
        {
            "design_images" => await designImages.Query()
                .Where(image => image.DeletedAt == null && image.CreatedAt >= from && image.CreatedAt < to)
                .Select(image => new UsageEvent(image.Product.UserId, image.CreatedAt!.Value, 0m))
                .ToListAsync(cancellationToken),
            "mockups" => await MockupEvents(mockup => mockup.SourceType == MockupSources.Generated, from, to, MockupRenderUsd, cancellationToken),
            "uploaded_mockups" => await MockupEvents(
                mockup => mockup.SourceType == MockupSources.Uploaded && mockup.ApiUsageRecordId == null, from, to, 0m, cancellationToken),
            "lifestyle_mockups" => await MockupEvents(
                mockup => mockup.SourceType == MockupSources.Uploaded && mockup.ApiUsageRecordId != null && mockup.Role == "Lifestyle",
                from, to, 0m, cancellationToken),
            "studio_keyframes" => await MockupEvents(mockup => mockup.SourceType == MockupSources.AiGenerated, from, to, 0m, cancellationToken),
            "listings" => await usageRecords.Query()
                .Where(record => record.Feature == UsageFeatures.Listing && record.CreatedAt >= from && record.CreatedAt < to)
                .Select(record => new UsageEvent(record.UserId, record.CreatedAt!.Value, record.CostUsd))
                .ToListAsync(cancellationToken),
            "standard_videos" => await VideoEvents("standard", from, to, VideoRenderUsd, cancellationToken),
            "ai_videos" => await VideoEvents("ai_shot", from, to, 0m, cancellationToken),
            "exports" => await exports.Query()
                .Where(export => export.CreatedAt >= from && export.CreatedAt < to)
                .Select(export => new UsageEvent(export.UserId, export.CreatedAt!.Value, 0m))
                .ToListAsync(cancellationToken),
            // Variation indexes start at 1: a second one for the same product needs Pro.
            "design_variations" => await designImages.Query()
                .Where(image => image.DeletedAt == null && image.VariationIndex >= 2 && image.CreatedAt >= from && image.CreatedAt < to)
                .Select(image => new UsageEvent(image.Product.UserId, image.CreatedAt!.Value, 0m))
                .ToListAsync(cancellationToken),
            // The config is jsonb, so the flag is read here rather than matched as text in SQL.
            "auto_approval" => (await batchJobs.Query()
                    .Where(job => job.DeletedAt == null && job.CreatedAt >= from && job.CreatedAt < to)
                    .Select(job => new { job.UserId, CreatedAt = job.CreatedAt!.Value, job.Config })
                    .ToListAsync(cancellationToken))
                .Where(job => SkipsReview(job.Config))
                .Select(job => new UsageEvent(job.UserId, job.CreatedAt, 0m))
                .ToList(),
            "custom_mockup_templates" => await mockupTemplates.Query()
                .Where(template => template.UserId != null && template.CreatedAt >= from && template.CreatedAt < to)
                .Select(template => new UsageEvent(template.UserId!.Value, template.CreatedAt!.Value, 0m))
                .ToListAsync(cancellationToken),
            "listing_regenerations" => await listingHistory.Query()
                .Where(entry => entry.GenerationNumber > 1 && entry.CreatedAt >= from && entry.CreatedAt < to)
                .Select(entry => new UsageEvent(entry.ListingContent.Product.UserId, entry.CreatedAt!.Value, 0m))
                .ToListAsync(cancellationToken),
            "landscape_videos" => await videos.Query()
                .Where(video => video.DeletedAt == null && video.AspectRatio == "16:9" && video.CreatedAt >= from && video.CreatedAt < to)
                .Select(video => new UsageEvent(video.Product.UserId, video.CreatedAt!.Value, 0m))
                .ToListAsync(cancellationToken),
            _ => [],
        };

    // A batch run with design review switched off, the Pro-only automatic approval.
    private static bool SkipsReview(string config)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(config)?[GenerationConfigKeys.RequireApproval]?.GetValue<bool>() == false;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private Task<List<UsageEvent>> MockupEvents(
        System.Linq.Expressions.Expression<Func<MockupImage, bool>> filter,
        DateTime from,
        DateTime to,
        decimal unitCost,
        CancellationToken cancellationToken) =>
        mockups.Query()
            .Where(filter)
            .Where(mockup => mockup.DeletedAt == null && mockup.CreatedAt >= from && mockup.CreatedAt < to)
            .Select(mockup => new UsageEvent(mockup.Product.UserId, mockup.CreatedAt!.Value, unitCost))
            .ToListAsync(cancellationToken);

    private Task<List<UsageEvent>> VideoEvents(string mode, DateTime from, DateTime to, decimal unitCost, CancellationToken cancellationToken) =>
        videos.Query()
            .Where(video => video.Mode == mode && video.DeletedAt == null && video.CreatedAt >= from && video.CreatedAt < to)
            .Select(video => new UsageEvent(video.Product.UserId, video.CreatedAt!.Value, unitCost))
            .ToListAsync(cancellationToken);

    /// <summary>Groups the Sellers with a plan in effect now by plan, with running trials apart.</summary>
    private async Task<List<Group>> LoadGroupsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var active = await subscriptions.Query()
            .Include(subscription => subscription.Plan)
            .ThenInclude(plan => plan.PlanFeatures)
            .Where(subscription => subscription.Status == "active"
                && subscription.DeletedAt == null
                && (subscription.TrialEndsAt == null || subscription.TrialEndsAt > now))
            .ToListAsync(cancellationToken);

        return active
            .GroupBy(subscription => subscription.TrialEndsAt is null ? subscription.Plan.Tier : TrialKey)
            .Select(group =>
            {
                var first = group.First();
                var isTrial = group.Key == TrialKey;
                return new Group(
                    group.Key,
                    isTrial ? $"{first.Plan.Name} trial" : first.Plan.Name,
                    isTrial,
                    first.Plan.IsActive ?? false,
                    isTrial ? 0m : first.Plan.MonthlyPriceUsd,
                    first.Plan.PlanFeatures
                        .Where(feature => PlanFeatures.IsKnown(feature.FeatureCode))
                        .ToDictionary(feature => feature.FeatureCode, feature => new PlanFeatureValue(feature.IsEnabled ?? false, feature.LimitValue)),
                    group.Select(subscription => subscription.UserId).ToHashSet());
            })
            .OrderBy(group => group.IsTrial)
            .ThenBy(group => group.MonthlyPriceUsd)
            .ToList();
    }

    private PlanGroupInsightDto BuildGroup(
        Group group,
        IReadOnlyDictionary<string, List<UsageEvent>> events,
        int days)
    {
        var subscribers = group.UserIds.Count;
        var entitlements = new PlanEntitlements(true, null, group.Name, group.Key, group.IsTrial, null, 0, 0, false, group.Features);

        var adoption = Metrics
            .Select(metric =>
            {
                var uses = events[metric.Key].Where(item => group.UserIds.Contains(item.UserId)).ToList();
                var usesPerUser = uses.GroupBy(item => item.UserId).Select(user => user.Count()).ToList();
                var included = metric.PlanFeature is null || entitlements.IsEnabled(metric.PlanFeature);

                // The same daily cap and day boundaries as the cap-hit card. The trial has allowances for its
                // whole length instead of daily caps.
                var cap = group.IsTrial ? null : Caps.Where(item => item.Metric == metric.Key).Select(item => entitlements.Limit(item.Code)).FirstOrDefault();
                int? usersAtCap = cap is int dailyCap
                    ? uses.GroupBy(item => (item.UserId, item.CreatedAt.Date)).Where(day => day.Count() >= dailyCap).Select(day => day.Key.UserId).Distinct().Count()
                    : null;

                return new FeatureAdoptionDto(
                    metric.Key,
                    metric.Label,
                    included,
                    usesPerUser.Count,
                    Percent(usesPerUser.Count, subscribers) ?? 0m,
                    usesPerUser.Count == 0 ? null : PlanVerdicts.Median(usesPerUser),
                    cap,
                    usersAtCap);
            })
            .ToList();

        // Cost is scaled to one month so it can be compared with the monthly price.
        var monthScale = 30m / days;
        var costByUser = Metrics
            .Where(metric => metric.CostsPlatform)
            .SelectMany(metric => events[metric.Key])
            .Where(item => group.UserIds.Contains(item.UserId))
            .GroupBy(item => item.UserId)
            .ToDictionary(item => item.Key, item => item.Sum(entry => entry.CostUsd) * monthScale);
        var costPerSubscriber = subscribers == 0 ? 0m : Math.Round(costByUser.Values.Sum() / subscribers, 4);

        return new PlanGroupInsightDto(
            group.Key,
            group.Name,
            group.IsTrial,
            subscribers,
            subscribers >= Rules.MinimumSample,
            Math.Round(Vnd(group.MonthlyPriceUsd)),
            adoption,
            new PlanCostDto(
                Vnd(costPerSubscriber),
                Math.Round(Vnd(group.MonthlyPriceUsd)),
                group.MonthlyPriceUsd > 0 ? Math.Round(costPerSubscriber / group.MonthlyPriceUsd * 100m, 1) : null));
    }

    /// <summary>
    /// Judges each paid plan (see <see cref="PlanVerdicts"/>). Retention is the Revenue page's, for
    /// the same days, so both pages always agree.
    /// </summary>
    private async Task<IReadOnlyList<PlanVerdictDto>> BuildVerdictsAsync(
        IReadOnlyList<Group> groups,
        IReadOnlyList<PlanGroupInsightDto> planInsights,
        IReadOnlyDictionary<string, List<UsageEvent>> events,
        DateTime from,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var charts = await revenueService.GetChartsAsync(
            new RevenueFilterDto
            {
                StartDate = DateOnly.FromDateTime(from).AddDays(1),
                EndDate = DateOnly.FromDateTime(now),
                Granularity = "Month"
            },
            cancellationToken);
        var retention = charts.IsSuccess
            ? charts.Value.ByPlan.ToDictionary(plan => plan.PlanName)
            : new Dictionary<string, RevenueByPlanDto>();

        var inputs = groups
            .Where(group => !group.IsTrial)
            .Select(group =>
            {
                var insight = planInsights.Single(plan => plan.Key == group.Key && !plan.IsTrial);
                var designs = CountPerUser(events["design_images"], group.UserIds);
                var mockups = CountPerUser(events["mockups"], group.UserIds);
                var flow = retention.GetValueOrDefault(group.Name);
                return new PlanVerdictInput(
                    group.Key,
                    group.Name,
                    group.OnSale,
                    insight.Subscribers,
                    insight.HasEnoughData,
                    insight.MonthlyPriceVnd,
                    insight.Adoption,
                    group.UserIds.Select(userId => new SellerUsage(designs.GetValueOrDefault(userId), mockups.GetValueOrDefault(userId))).ToList(),
                    flow?.Customers.Start ?? 0,
                    flow is null ? 0 : flow.Customers.Stayed + flow.Customers.SwitchedOut,
                    flow?.RetentionRate);
            })
            .ToList();

        return PlanVerdicts.Build(inputs, Rules);
    }

    /// <summary>
    /// Compares each paid plan's customers who stopped paying with those who stayed (see
    /// <see cref="PlanChurnSignals"/>), over the same customers and days as the verdicts' retention.
    /// </summary>
    private async Task<IReadOnlyList<PlanChurnSignalDto>> BuildChurnSignalsAsync(
        IReadOnlyList<Group> groups,
        IReadOnlyDictionary<string, List<UsageEvent>> allEvents,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken)
    {
        var cohorts = await revenueService.GetPlanCohortsAsync(periodStart, periodEnd, cancellationToken);
        if (!cohorts.IsSuccess) return [];

        var attempts = await LoadAttemptsAsync(
            periodStart.AddDays(-PlanChurnSignals.HistoryDays).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            cancellationToken);
        return groups
            .Where(group => !group.IsTrial)
            .Select(group =>
            {
                var cohort = cohorts.Value.FirstOrDefault(item => item.PlanName == group.Name)
                    ?? new PlanCohortDto(group.Name, [], []);
                var entitlements = new PlanEntitlements(true, null, group.Name, group.Key, false, null, 0, 0, false, group.Features);
                var features = Metrics
                    .Select(metric => new ChurnFeature(
                        metric.Key,
                        metric.Label,
                        metric.PlanFeature is null || entitlements.IsEnabled(metric.PlanFeature),
                        allEvents[metric.Key].Select(item => new SellerUse(item.UserId, item.CreatedAt)).ToList(),
                        attempts.GetValueOrDefault(metric.Key),
                        Caps.Where(item => item.Metric == metric.Key).Select(item => entitlements.Limit(item.Code)).FirstOrDefault()))
                    .ToList();
                return PlanChurnSignals.Build(group.Key, group.Name, cohort, periodStart, periodEnd, features);
            })
            .ToList();
    }

    /// <summary>
    /// Every attempt, failed or not, at the features whose failures are recorded: the AI calls in
    /// api_usage_records and the video renders in promo_videos.
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyList<SellerAttempt>>> LoadAttemptsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var calls = await usageRecords.Query()
            .Where(record => record.CreatedAt >= from && record.CreatedAt < to
                && (record.Feature == UsageFeatures.ImageGeneration || record.Feature == UsageFeatures.Listing || record.Feature == UsageFeatures.Mockup))
            .Select(record => new { record.Feature, record.UserId, CreatedAt = record.CreatedAt!.Value, record.Status })
            .ToListAsync(cancellationToken);
        var renders = await videos.Query()
            .Where(video => video.DeletedAt == null && video.CreatedAt >= from && video.CreatedAt < to)
            .Select(video => new { video.Mode, video.Product.UserId, CreatedAt = video.CreatedAt!.Value, video.Status })
            .ToListAsync(cancellationToken);

        IReadOnlyList<SellerAttempt> Calls(string feature) => calls
            .Where(call => call.Feature == feature)
            .Select(call => new SellerAttempt(call.UserId, call.CreatedAt, call.Status != "success"))
            .ToList();
        IReadOnlyList<SellerAttempt> Renders(string mode) => renders
            .Where(render => render.Mode == mode)
            .Select(render => new SellerAttempt(render.UserId, render.CreatedAt, render.Status == "failed"))
            .ToList();

        return new Dictionary<string, IReadOnlyList<SellerAttempt>>
        {
            ["design_images"] = Calls(UsageFeatures.ImageGeneration),
            ["listings"] = Calls(UsageFeatures.Listing),
            ["lifestyle_mockups"] = Calls(UsageFeatures.Mockup),
            ["standard_videos"] = Renders("standard"),
            ["ai_videos"] = Renders("ai_shot"),
        };
    }

    private static Dictionary<Guid, int> CountPerUser(IEnumerable<UsageEvent> items, IReadOnlySet<Guid> userIds) =>
        items
            .Where(item => userIds.Contains(item.UserId))
            .GroupBy(item => item.UserId)
            .ToDictionary(item => item.Key, item => item.Count());

    private async Task<TrialInsightDto> BuildTrialAsync(
        DateTime from,
        DateTime now,
        int days,
        IReadOnlyDictionary<string, List<UsageEvent>> events,
        CancellationToken cancellationToken)
    {
        var previousFrom = from.AddDays(-days);
        var trials = await subscriptions.Query()
            .Where(subscription => subscription.TrialEndsAt != null && subscription.CreatedAt >= previousFrom && subscription.CreatedAt < now)
            .ToListAsync(cancellationToken);

        var trialUserIds = trials.Select(trial => trial.UserId).Distinct().ToList();
        var paidInvoices = await invoices.Query()
            .Include(invoice => invoice.Subscription)
            .ThenInclude(subscription => subscription.Plan)
            .Where(invoice => trialUserIds.Contains(invoice.UserId)
                && invoice.Status == "paid"
                && invoice.Subscription.TrialEndsAt == null)
            .ToListAsync(cancellationToken);

        // A trial converts when its Seller pays for a plan while it runs; when it ends the Seller is on Free.
        string? ConvertedTier(Subscription trial)
        {
            var start = DateOnly.FromDateTime(trial.CreatedAt ?? trial.StartDate.ToDateTime(TimeOnly.MinValue));
            var end = DateOnly.FromDateTime(trial.TrialEndsAt!.Value);
            return paidInvoices
                .Where(invoice => invoice.UserId == trial.UserId)
                .Where(invoice => (invoice.PaymentDate ?? invoice.InvoiceDate) >= start && (invoice.PaymentDate ?? invoice.InvoiceDate) <= end)
                .OrderBy(invoice => invoice.PaymentDate ?? invoice.InvoiceDate)
                .Select(invoice => invoice.Subscription.Plan.Tier)
                .FirstOrDefault();
        }

        // The name of the plan a trial bought, for the per-plan breakdown.
        string PlanNameOf(string tier) =>
            paidInvoices.FirstOrDefault(invoice => invoice.Subscription.Plan.Tier == tier)?.Subscription.Plan.Name ?? tier;

        var current = trials.Where(trial => trial.CreatedAt >= from).ToList();
        var previous = trials.Where(trial => trial.CreatedAt < from).ToList();
        var outcomes = current.Select(trial => (Trial: trial, Tier: ConvertedTier(trial))).ToList();

        // A trial that has not bought is finished once it has expired or been cancelled; one still
        // running has no outcome yet, so it counts in neither part of the conversion rate.
        bool Finished(Subscription trial) => trial.TrialEndsAt <= now || trial.Status != "active";

        var converted = outcomes.Where(item => item.Tier is not null).ToList();
        var ended = outcomes.Where(item => item.Tier is null && Finished(item.Trial)).ToList();
        var running = outcomes.Count(item => item.Tier is null && !Finished(item.Trial));
        var previousOutcomes = previous.Select(trial => (Trial: trial, Converted: ConvertedTier(trial) is not null)).ToList();

        // What the trials that bought and the finished ones that did not did during their trial.
        bool UsedDuringTrial(string metric, Subscription trial) =>
            events[metric].Any(item => item.UserId == trial.UserId && item.CreatedAt >= (trial.CreatedAt ?? from) && item.CreatedAt <= trial.TrialEndsAt);
        var comparison = Metrics
            .Concat(ProTrialMetrics)
            .Select(metric => new TrialFeatureComparisonDto(
                metric.Key,
                metric.Label,
                Percent(converted.Count(item => UsedDuringTrial(metric.Key, item.Trial)), converted.Count),
                Percent(ended.Count(item => UsedDuringTrial(metric.Key, item.Trial)), ended.Count)))
            .ToList();

        return new TrialInsightDto(
            current.Count,
            running,
            converted.Count(item => item.Tier == SubscriptionTrialTiers.Pro),
            converted.Count(item => item.Tier == SubscriptionTrialTiers.Starter),
            converted.Count(item => item.Tier is not (SubscriptionTrialTiers.Pro or SubscriptionTrialTiers.Starter)),
            ended.Count,
            TrialConversion.Rate(converted.Count, ended.Count),
            TrialConversion.Rate(
                previousOutcomes.Count(item => item.Converted),
                previousOutcomes.Count(item => !item.Converted && Finished(item.Trial))),
            comparison,
            converted
                .GroupBy(item => item.Tier!)
                .Select(group => new TrialConversionByPlanDto(group.Key, PlanNameOf(group.Key), group.Count()))
                .OrderByDescending(item => item.Count)
                .ToList());
    }

    private async Task<IReadOnlyList<PipelineStageDto>> BuildPipelineAsync(DateTime from, DateTime now, CancellationToken cancellationToken)
    {
        var productIds = await products.Query()
            .Where(product => product.DeletedAt == null && product.CreatedAt >= from && product.CreatedAt < now)
            .Select(product => product.Id)
            .ToListAsync(cancellationToken);

        // Each step of a workflow run, read from what it leaves behind: a step's node run is not always
        // marked (the design steps run as a batch job before the run starts).
        var prompted = await prompts.Query()
            .Where(prompt => productIds.Contains(prompt.ProductId))
            .Select(prompt => prompt.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var designs = await designImages.Query()
            .Where(image => image.DeletedAt == null && productIds.Contains(image.ProductId))
            .Select(image => new { image.ProductId, image.ApprovalStatus })
            .ToListAsync(cancellationToken);
        var mockupRows = await mockups.Query()
            .Where(mockup => mockup.DeletedAt == null && productIds.Contains(mockup.ProductId))
            .Select(mockup => new { mockup.ProductId, mockup.ApprovalStatus })
            .Distinct()
            .ToListAsync(cancellationToken);
        var videoRows = await videos.Query()
            .Where(video => video.DeletedAt == null && video.Status == "completed" && productIds.Contains(video.ProductId))
            .Select(video => new { video.ProductId, video.ApprovalStatus })
            .Distinct()
            .ToListAsync(cancellationToken);
        var exported = await runs.Query()
            .Where(run => productIds.Contains(run.ProductId)
                && exports.Query().Any(export => export.WorkflowRunId == run.Id && export.Status != "failed"))
            .Select(run => run.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var listed = await listings.Query()
            .Where(listing => productIds.Contains(listing.ProductId))
            .Select(listing => listing.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var published = (await etsyUploads.Query()
                .Where(upload => upload.UploadStatus == "success" && productIds.Contains(upload.ProductId))
                .Select(upload => upload.ProductId)
                .ToListAsync(cancellationToken))
            .Concat(await printifyUploads.Query()
                .Where(upload => upload.UploadStatus == "success" && productIds.Contains(upload.ProductId))
                .Select(upload => upload.ProductId)
                .ToListAsync(cancellationToken));

        // The publish steps are on the canvas before their executors exist; the page says so.
        var canPublish = workflowCapabilities.Get().Nodes
            .Any(node => node.Type is "publish-etsy" or "publish-printify" && node.Enabled);

        // In run order, from the Seller's prompt to the push to a store. Listing content runs beside
        // the video, but a store needs it, so it sits just before publishing. Each step counts only
        // the products that also reached every step before it.
        (string Key, string Label, IReadOnlySet<Guid> Products, bool Available)[] steps =
        [
            ("prompt_added", "Prompt added", productIds.ToHashSet(), true),
            ("prompt_written", "Prompt written", prompted.ToHashSet(), true),
            ("designed", "Design generated", designs.Select(item => item.ProductId).ToHashSet(), true),
            ("design_approved", "Design approved", designs.Where(item => item.ApprovalStatus == "approved").Select(item => item.ProductId).ToHashSet(), true),
            ("mockups", "Mockups applied", mockupRows.Select(item => item.ProductId).ToHashSet(), true),
            ("mockups_approved", "Mockups approved", mockupRows.Where(item => item.ApprovalStatus == "approved").Select(item => item.ProductId).ToHashSet(), true),
            ("video", "Video generated", videoRows.Select(item => item.ProductId).ToHashSet(), true),
            ("video_approved", "Video approved", videoRows.Where(item => item.ApprovalStatus == "approved").Select(item => item.ProductId).ToHashSet(), true),
            ("exported", "Exported (ZIP)", exported.ToHashSet(), true),
            ("listing", "Listing written", listed.ToHashSet(), true),
            ("published", "Published to store", published.ToHashSet(), canPublish),
        ];
        var counts = PipelineFunnel.Nest(steps.Select(step => step.Products).ToList());
        return steps.Select((step, index) => new PipelineStageDto(step.Key, step.Label, counts[index], step.Available)).ToList();
    }

    private static decimal? Percent(int part, int whole) =>
        whole == 0 ? null : Math.Round((decimal)part / whole * 100m, 1);

    private PlanVerdictOptions Rules => verdictOptions.Value;

    // Costs are measured in USD; the page shows them in VND at the PayOS rate, to two decimals so
    // a fraction-of-a-cent unit cost does not read as zero.
    private decimal Vnd(decimal usd) => Math.Round(usd * gatewaySettings.Value.UsdToVndRate, 2);
}
