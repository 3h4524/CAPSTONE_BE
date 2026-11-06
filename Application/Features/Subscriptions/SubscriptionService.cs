using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.Features.Subscriptions.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Subscriptions;

/// <summary>
/// Configuration <see cref="SubscriptionService"/> needs for real PayOS checkouts.
/// </summary>
/// <remarks>
/// A narrow Application-owned projection of <c>PayOsOptions</c> (Infrastructure), so Application
/// never references the Infrastructure options type directly.
/// </remarks>
public sealed record PaymentGatewaySettings(decimal UsdToVndRate, int? TestAmountVnd)
{
    /// <summary>A stored USD amount in whole đồng, as PayOS charges it.</summary>
    public decimal ToVnd(decimal usd) => Math.Round(usd * UsdToVndRate);
}

/// <summary>
/// Implements the Subscriptions feature's use cases.
/// </summary>
public sealed class SubscriptionService(
    ISubscriptionRepository subscriptions,
    IPlanRepository plans,
    IInvoiceRepository invoices,
    IUsageStatisticRepository usageStatistics,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPaymentGatewayClient paymentGateway,
    IInvoicePdfRenderer invoicePdfRenderer,
    IOptions<PaymentGatewaySettings> gatewaySettings,
    TimeProvider timeProvider,
    IValidator<CheckoutRequestDto> checkoutValidator,
    IValidator<UpgradeRequestDto> upgradeValidator,
    IValidator<DowngradeRequestDto> downgradeValidator,
    IPlanEntitlementService entitlements,
    IFeatureCatalog featureCatalog,
    IPlanSettings planSettings,
    ILogger<SubscriptionService> logger)
    : ISubscriptionService
{
    private const decimal FreeTierMaxPrice = 0m;


    /// <inheritdoc />
    public async Task<Result<SubscriptionOverviewResponseDto>> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Result.Failure<SubscriptionOverviewResponseDto>(SubscriptionErrors.Unauthenticated());
        }

        var activeSubscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
        var hasActivePaidPlan = IsPaidPlan(activeSubscription);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var usage = activeSubscription is null
            ? null
            : await usageStatistics.GetCurrentPeriodAsync(userId, today, cancellationToken);

        var usageQuotas = activeSubscription is null
            ? []
            : QuotaDefinitions.Build(usage);

        // One trial per account, and never for a Seller who already pays.
        var trialEligible = !hasActivePaidPlan
            && activeSubscription?.TrialEndsAt is null
            && !await subscriptions.HasUsedTrialAsync(userId, cancellationToken);

        var comparisonPlans = await plans.GetComparisonPlansAsync(activeSubscription?.PlanId, cancellationToken);
        var recommendedTier = await planSettings.GetRecommendedPlanTierAsync(cancellationToken);
        var availablePlans = comparisonPlans
            .Select(plan => MapToAvailablePlanDto(plan, activeSubscription?.PlanId, gatewaySettings.Value) with
            {
                IsRecommended = recommendedTier != null && plan.Tier == recommendedTier
            })
            .ToList();

        var recentInvoices = await invoices.GetRecentWithPlanAsync(userId, take: 3, cancellationToken);
        var recentInvoiceDtos = recentInvoices.Select(invoice => MapToRecentInvoiceDto(invoice, gatewaySettings.Value)).ToList();

        var trialTier = await planSettings.GetTrialPlanTierAsync(cancellationToken);
        var trialAllowances = (await planSettings.GetAllowancesAsync(cancellationToken)).Trial;
        var response = new SubscriptionOverviewResponseDto(
            activeSubscription is null ? null : MapToCurrentSubscriptionDto(activeSubscription, today, gatewaySettings.Value),
            usageQuotas,
            availablePlans,
            recentInvoiceDtos,
            hasActivePaidPlan,
            trialEligible,
            await planSettings.GetTrialDaysAsync(cancellationToken),
            availablePlans.FirstOrDefault(plan => plan.Tier == trialTier)?.Name,
            [
                new TrialAllowanceDto(UsageAllowances.Products, UsageAllowances.Label(UsageAllowances.Products), trialAllowances.Products),
                new TrialAllowanceDto(UsageAllowances.Mockups, UsageAllowances.Label(UsageAllowances.Mockups), trialAllowances.Mockups),
                new TrialAllowanceDto(UsageAllowances.Videos, UsageAllowances.Label(UsageAllowances.Videos), trialAllowances.Videos),
                new TrialAllowanceDto(UsageAllowances.Listings, UsageAllowances.Label(UsageAllowances.Listings), trialAllowances.Listings),
            ]);

        return Result.Success(response);
    }

    /// <inheritdoc />
    public async Task<Result<TrialResponseDto>> StartTrialAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Result.Failure<TrialResponseDto>(SubscriptionErrors.Unauthenticated());
        }

        var activeSubscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
        if (activeSubscription is not null)
        {
            return Result.Failure<TrialResponseDto>(SubscriptionErrors.TrialNotAvailableWithPlan());
        }

        if (await subscriptions.HasUsedTrialAsync(userId, cancellationToken))
        {
            return Result.Failure<TrialResponseDto>(SubscriptionErrors.TrialAlreadyUsed());
        }

        // Design images run on the Seller's own Gemini key, so a trial without one cannot make anything.
        var keyCheck = await entitlements.RequireGeminiKeyAsync(userId, cancellationToken);
        if (keyCheck.IsFailure)
        {
            return Result.Failure<TrialResponseDto>(keyCheck.Error);
        }

        // The plan the trial gives is set by an Admin.
        var plan = await plans.GetPurchasableByTierAsync(await planSettings.GetTrialPlanTierAsync(cancellationToken), cancellationToken);
        if (plan is null)
        {
            return Result.Failure<TrialResponseDto>(SubscriptionErrors.TrialPlanUnavailable());
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(utcNow);
        // As long as an Admin set it now; a later change leaves this trial's end where it is.
        var trialEndsAt = utcNow.AddDays(await planSettings.GetTrialDaysAsync(cancellationToken));

        // A trial is free: it has no invoice, carries no price, and never renews by itself, so
        // revenue, MRR and churn never count it.
        var trial = new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanId = plan.Id,
            BillingCycle = "monthly",
            MonthlyPriceUsd = 0m,
            AnnualPriceUsd = null,
            Status = SubscriptionStatuses.Active,
            StartDate = today,
            RenewalDate = DateOnly.FromDateTime(trialEndsAt),
            TrialEndsAt = trialEndsAt,
            AutoRenew = false,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        await subscriptions.AddAsync(trial, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new TrialResponseDto(trial.Id, plan.Name, trialEndsAt));
    }

    /// <inheritdoc />
    public async Task<Result<EntitlementsResponseDto>> GetEntitlementsAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Result.Failure<EntitlementsResponseDto>(SubscriptionErrors.Unauthenticated());
        }

        var current = await entitlements.GetAsync(userId, cancellationToken);
        // Free and the Pro trial have allowances instead of daily caps, so no cap applies to them.
        int? LimitOf(string code) =>
            (current.IsFree || current.IsTrial) && code is PlanFeatures.DailyMockupCap or PlanFeatures.DailyListingCap or PlanFeatures.DailyVideoCap
                ? null
                : current.Limit(code);
        var features = (await featureCatalog.GetAllAsync(cancellationToken))
            .Select(feature => new EntitlementFeatureDto(
                feature.Code,
                feature.Label,
                feature.Kind,
                feature.Group,
                feature.Kind == PlanFeatureKinds.Flag ? current.IsEnabled(feature.Code) : LimitOf(feature.Code) != 0,
                feature.Kind == PlanFeatureKinds.Limit ? LimitOf(feature.Code) : null))
            .ToList();

        return Result.Success(new EntitlementsResponseDto(
            current.HasPlan,
            current.PlanName,
            current.Tier,
            current.IsTrial,
            current.TrialEndsAt,
            current.MaxBatchSize,
            current.MaxConcurrentJobs,
            current.PrioritySupport,
            features));
    }

    /// <inheritdoc />
    public async Task<Result<CheckoutResponseDto>> InitiateCheckoutAsync(
        CheckoutRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await checkoutValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<CheckoutResponseDto>(validation.ToValidationError());
        }

        if (!TryGetUserId(out var userId))
        {
            return Result.Failure<CheckoutResponseDto>(SubscriptionErrors.Unauthenticated());
        }

        var activeSubscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);

        // BR190: Buy is only for a Seller with no active plan or one on the $0/Free tier; a
        // Seller already on a paid plan must use Upgrade/Downgrade (out of this scope), or Renew
        // when it is past due.
        if (IsPaidPlan(activeSubscription))
        {
            return Result.Failure<CheckoutResponseDto>(SubscriptionErrors.AlreadySubscribed());
        }

        var plan = await plans.GetPurchasableByIdAsync(request.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<CheckoutResponseDto>(SubscriptionErrors.PlanNotFound());
        }

        var billingCycle = request.BillingCycle.Trim().ToLowerInvariant();
        if (billingCycle == "annual" && plan.AnnualPriceUsd is null)
        {
            return Result.Failure<CheckoutResponseDto>(SubscriptionErrors.AnnualNotAvailable());
        }

        var settings = gatewaySettings.Value;
        var priceUsd = billingCycle == "annual" ? plan.AnnualPriceUsd!.Value : plan.MonthlyPriceUsd;
        // PayOS has no sandbox — every checkout is a real transfer. TestAmountVnd lets a dev
        // environment cap every charge to a tiny fixed amount instead of the real converted price.
        var amountVnd = settings.TestAmountVnd ?? (int)Math.Round(priceUsd * settings.UsdToVndRate);
        var orderCode = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var description = $"Sub {plan.Tier}".Length > 25 ? plan.Tier[..Math.Min(plan.Tier.Length, 20)] : $"Sub {plan.Tier}";

        PaymentLinkResult paymentLink;
        try
        {
            paymentLink = await paymentGateway.CreatePaymentLinkAsync(
                orderCode, amountVnd, description, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<CheckoutResponseDto>(SubscriptionErrors.GatewayUnavailable());
        }

        var utcNow = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(utcNow.UtcDateTime);
        var renewalDate = billingCycle == "annual" ? today.AddYears(1) : today.AddMonths(1);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // A trial keeps running until this checkout is paid (FinalizeCheckoutAsync retires
            // it then), so abandoning the payment does not cost the Seller the rest of the trial.
            if (activeSubscription is not null && activeSubscription.TrialEndsAt is null)
            {
                activeSubscription.Status = SubscriptionStatuses.Cancelled;
                activeSubscription.CancelledAt = utcNow.UtcDateTime;
                await subscriptions.UpdateAsync(activeSubscription, cancellationToken: cancellationToken);
            }

            var newSubscription = new Subscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = plan.Id,
                BillingCycle = billingCycle,
                MonthlyPriceUsd = plan.MonthlyPriceUsd,
                AnnualPriceUsd = plan.AnnualPriceUsd,
                Status = SubscriptionStatuses.AwaitingPayment,
                StartDate = today,
                RenewalDate = renewalDate,
                AutoRenew = true,
                CreatedAt = utcNow.UtcDateTime
            };
            await subscriptions.AddAsync(newSubscription, cancellationToken: cancellationToken);

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                SubscriptionId = newSubscription.Id,
                UserId = userId,
                InvoiceNumber = GenerateInvoiceNumber(utcNow),
                InvoiceDate = today,
                DueDate = today,
                AmountUsd = priceUsd,
                TaxAmount = 0m,
                TotalAmount = priceUsd,
                Status = InvoiceStatuses.AwaitingPayment,
                PayosOrderCode = orderCode,
                PayosPaymentLinkId = paymentLink.PaymentLinkId,
                PayosQrCode = paymentLink.QrCode,
                Items = System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    new { description = $"{plan.Name} plan ({billingCycle} billing)", amountVnd }
                }),
                CreatedAt = utcNow.UtcDateTime
            };
            await invoices.AddAsync(invoice, cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success(new CheckoutResponseDto(
                invoice.Id, paymentLink.QrCode, paymentLink.CheckoutUrl, amountVnd));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Result<CheckoutStatusResponseDto>> GetCheckoutStatusAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Result.Failure<CheckoutStatusResponseDto>(SubscriptionErrors.Unauthenticated());
        }

        var invoice = await invoices.GetByIdForUserAsync(invoiceId, userId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<CheckoutStatusResponseDto>(SubscriptionErrors.InvoiceNotFound());
        }

        // Fast path: the webhook already resolved this checkout, no need to call PayOS again.
        if (invoice.Status != InvoiceStatuses.AwaitingPayment)
        {
            return Result.Success(MapToStatusDto(invoice));
        }

        // Reconciliation: a webhook can be lost (a real risk behind a local ngrok tunnel), so a
        // checkout that has stayed pending for a while is actively re-checked with PayOS itself.
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var createdAt = invoice.CreatedAt ?? utcNow;
        if (utcNow - createdAt < TimeSpan.FromSeconds(10) || invoice.PayosOrderCode is null)
        {
            return Result.Success(MapToStatusDto(invoice));
        }

        PaymentLinkStatusResult gatewayStatus;
        try
        {
            gatewayStatus = await paymentGateway.GetPaymentLinkStatusAsync(
                invoice.PayosOrderCode.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // PayOS being briefly unreachable should not fail the poll; the client just tries again.
            return Result.Success(MapToStatusDto(invoice));
        }

        if (gatewayStatus.IsPaid)
        {
            await FinalizeCheckoutAsync(invoice, CheckoutOutcome.Paid, cancellationToken);
        }
        else if (gatewayStatus.IsFailed)
        {
            await FinalizeCheckoutAsync(invoice, CheckoutOutcome.Declined, cancellationToken);
        }

        return Result.Success(MapToStatusDto(invoice));
    }

    /// <inheritdoc />
    public async Task HandlePayOsWebhookAsync(
        string rawJsonBody,
        CancellationToken cancellationToken = default)
    {
        var verification = await paymentGateway.VerifyWebhookAsync(rawJsonBody, cancellationToken);
        if (!verification.IsValid)
        {
            return;
        }

        var invoice = await invoices.GetByPayosOrderCodeAsync(verification.OrderCode, cancellationToken);
        // Idempotent: PayOS may retry webhooks, and a checkout that already resolved (via a
        // previous webhook delivery or a status-poll reconciliation) must not be re-applied.
        if (invoice is null || invoice.Status != InvoiceStatuses.AwaitingPayment)
        {
            return;
        }

        await FinalizeCheckoutAsync(
            invoice,
            verification.Success ? CheckoutOutcome.Paid : CheckoutOutcome.Declined,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> CancelCheckoutAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Result.Failure(SubscriptionErrors.Unauthenticated());
        }

        var invoice = await invoices.GetByIdForUserAsync(invoiceId, userId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure(SubscriptionErrors.InvoiceNotFound());
        }

        // Idempotent: already resolved (paid, or already cancelled) — nothing to do.
        if (invoice.Status != InvoiceStatuses.AwaitingPayment)
        {
            return Result.Success();
        }

        // Closing the page does not stop a payment: a Seller can pay and close it before the poll
        // sees the money, so the invoice is voided only once the link can no longer be paid.
        var closure = invoice.PayosOrderCode is { } orderCode
            ? await ClosePaymentLinkAsync(orderCode, "Seller cancelled checkout", cancellationToken)
            : PaymentLinkClosure.Closed;
        if (closure == PaymentLinkClosure.Paid)
        {
            await FinalizeCheckoutAsync(invoice, CheckoutOutcome.Paid, cancellationToken);
        }
        else if (closure == PaymentLinkClosure.Closed)
        {
            await FinalizeCheckoutAsync(invoice, CheckoutOutcome.Cancelled, cancellationToken);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<CheckoutResponseDto>> RenewAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Result.Failure<CheckoutResponseDto>(SubscriptionErrors.Unauthenticated());
        }

        var current = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
        if (!IsPaidPlan(current))
        {
            return Result.Failure<CheckoutResponseDto>(SubscriptionErrors.NothingToRenew());
        }

        var utcNow = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(utcNow.UtcDateTime);
        if (!RenewalRules.CanRenew(current!.RenewalDate, today))
        {
            return Result.Failure<CheckoutResponseDto>(
                SubscriptionErrors.RenewalNotDue(current.RenewalDate.AddDays(-RenewalRules.RenewWindowDays)));
        }

        var (plan, billingCycle, priceUsd) = PriceNextCycle(current);
        var settings = gatewaySettings.Value;
        var amountVnd = settings.TestAmountVnd ?? (int)Math.Round(priceUsd * settings.UsdToVndRate);
        var orderCode = utcNow.ToUnixTimeMilliseconds();
        var description = $"Renew {plan.Tier}".Length > 25 ? plan.Tier[..Math.Min(plan.Tier.Length, 20)] : $"Renew {plan.Tier}";

        PaymentLinkResult paymentLink;
        try
        {
            paymentLink = await paymentGateway.CreatePaymentLinkAsync(orderCode, amountVnd, description, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<CheckoutResponseDto>(SubscriptionErrors.GatewayUnavailable());
        }

        // The renewal is billed on the subscription itself, so revenue counts it as a renewal and
        // nothing about the current plan changes until it is paid.
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            SubscriptionId = current.Id,
            UserId = userId,
            InvoiceNumber = GenerateInvoiceNumber(utcNow),
            InvoiceDate = today,
            DueDate = current.RenewalDate,
            AmountUsd = priceUsd,
            TaxAmount = 0m,
            TotalAmount = priceUsd,
            Status = InvoiceStatuses.AwaitingPayment,
            PayosOrderCode = orderCode,
            PayosPaymentLinkId = paymentLink.PaymentLinkId,
            PayosQrCode = paymentLink.QrCode,
            Items = new RenewalInvoiceItem(
                $"{plan.Name} plan renewal ({billingCycle} billing)", amountVnd, plan.Id, billingCycle,
                current.PlanId, current.BillingCycle).ToItemsJson(),
            CreatedAt = utcNow.UtcDateTime
        };
        await invoices.AddAsync(invoice, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CheckoutResponseDto(
            invoice.Id, paymentLink.QrCode, paymentLink.CheckoutUrl, amountVnd));
    }

    /// <inheritdoc />
    public async Task<Result<UpgradeResponseDto>> UpgradeAsync(
        UpgradeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await upgradeValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<UpgradeResponseDto>(validation.ToValidationError());
        }

        if (!TryGetUserId(out var userId))
        {
            return Result.Failure<UpgradeResponseDto>(SubscriptionErrors.Unauthenticated());
        }

        var current = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);

        // Upgrading requires an active paid plan.
        if (!IsPaidPlan(current))
        {
            return Result.Failure<UpgradeResponseDto>(SubscriptionErrors.NoActivePlanToChange());
        }

        // Proration credits the unused rest of the paid cycle; a past-due plan has none left.
        if (RenewalRules.IsPastDue(current!.RenewalDate, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)))
        {
            return Result.Failure<UpgradeResponseDto>(SubscriptionErrors.RenewBeforeUpgrade());
        }

        var targetPlan = await plans.GetPurchasableByIdAsync(request.PlanId, cancellationToken);
        if (targetPlan is null)
        {
            return Result.Failure<UpgradeResponseDto>(SubscriptionErrors.PlanNotFound());
        }

        // The target must be a strictly higher tier than the current plan. Tier level is modeled
        // as monthly price ordering throughout this feature (see GetComparisonPlansAsync).
        if (targetPlan.MonthlyPriceUsd <= current!.Plan.MonthlyPriceUsd)
        {
            return Result.Failure<UpgradeResponseDto>(SubscriptionErrors.TargetNotHigherTier());
        }

        if (current.BillingCycle == "annual" && targetPlan.AnnualPriceUsd is null)
        {
            return Result.Failure<UpgradeResponseDto>(SubscriptionErrors.AnnualNotAvailable());
        }

        var utcNow = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(utcNow.UtcDateTime);
        var settings = gatewaySettings.Value;

        var newPlanPrice = current.BillingCycle == "annual"
            ? targetPlan.AnnualPriceUsd!.Value
            : targetPlan.MonthlyPriceUsd;
        var currentPlanPrice = current.BillingCycle == "annual"
            ? current.AnnualPriceUsd ?? current.MonthlyPriceUsd
            : current.MonthlyPriceUsd;

        // Only the remaining days of the current cycle are charged at the new plan's rate;
        // the current plan's own unused-day value is credited against that same remaining
        // period, so Due today is the prorated *difference*, not the new plan's full price. This
        // is what makes Due today reach exactly $0.00 when an upgrade happens on the renewal date
        // itself (remainingDays == 0), so the amount due reaches exactly $0.00 on the renewal date itself.
        var totalCycleDays = Math.Max(1, current.RenewalDate.DayNumber - current.StartDate.DayNumber);
        var remainingDays = Math.Clamp(current.RenewalDate.DayNumber - today.DayNumber, 0, totalCycleDays);
        var proratedNewCharge = newPlanPrice * remainingDays / totalCycleDays;
        var creditApplied = Math.Round(currentPlanPrice * remainingDays / totalCycleDays, 2, MidpointRounding.AwayFromZero);
        var dueToday = Math.Max(0m, Math.Round(proratedNewCharge, 2, MidpointRounding.AwayFromZero) - creditApplied);

        var itemsJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { description = $"{targetPlan.Name} plan ({current.BillingCycle} billing)", amountVnd = (int)Math.Round(newPlanPrice * settings.UsdToVndRate) },
            new { description = "Prorated credit from current plan", amountVnd = -(int)Math.Round(creditApplied * settings.UsdToVndRate) }
        });

        // Due today of $0.00 activates immediately with no new payment charge.
        if (dueToday <= 0m)
        {
            current.PlanId = targetPlan.Id;
            current.Plan = targetPlan;
            current.MonthlyPriceUsd = targetPlan.MonthlyPriceUsd;
            current.AnnualPriceUsd = targetPlan.AnnualPriceUsd;
            current.UpdatedAt = utcNow.UtcDateTime;
            await subscriptions.UpdateAsync(current, cancellationToken: cancellationToken);

            var freeInvoice = new Invoice
            {
                Id = Guid.NewGuid(),
                SubscriptionId = current.Id,
                UserId = userId,
                InvoiceNumber = GenerateInvoiceNumber(utcNow),
                InvoiceDate = today,
                DueDate = today,
                AmountUsd = dueToday,
                TaxAmount = 0m,
                TotalAmount = dueToday,
                Status = InvoiceStatuses.Paid,
                PaymentDate = today,
                Items = itemsJson,
                CreatedAt = utcNow.UtcDateTime
            };
            await invoices.AddAsync(freeInvoice, cancellationToken: cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new UpgradeResponseDto(
                PaymentRequired: false,
                DueTodayUsd: 0m,
                ProratedCreditUsd: creditApplied,
                MonthlyRate: newPlanPrice,
                FirstRenewalDate: current.RenewalDate,
                InvoiceId: freeInvoice.Id,
                QrCode: null,
                CheckoutUrl: null,
                AmountVnd: null,
                DueTodayVnd: 0m,
                ProratedCreditVnd: settings.ToVnd(creditApplied),
                MonthlyRateVnd: settings.ToVnd(newPlanPrice)));
        }

        // An amount is due — proceed through the payment flow before activating. The current
        // plan is left untouched and Active (unlike Buy, which cancels the old one immediately)
        // so the Seller keeps full access if this payment never completes; FinalizeCheckoutAsync
        // retires the old subscription once this one is actually paid.
        var amountVnd = settings.TestAmountVnd ?? (int)Math.Round(dueToday * settings.UsdToVndRate);
        var orderCode = utcNow.ToUnixTimeMilliseconds();
        var description = $"Sub {targetPlan.Tier}".Length > 25
            ? targetPlan.Tier[..Math.Min(targetPlan.Tier.Length, 20)]
            : $"Sub {targetPlan.Tier}";

        PaymentLinkResult paymentLink;
        try
        {
            paymentLink = await paymentGateway.CreatePaymentLinkAsync(orderCode, amountVnd, description, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<UpgradeResponseDto>(SubscriptionErrors.GatewayUnavailable());
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var pendingSubscription = new Subscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = targetPlan.Id,
                BillingCycle = current.BillingCycle,
                MonthlyPriceUsd = targetPlan.MonthlyPriceUsd,
                AnnualPriceUsd = targetPlan.AnnualPriceUsd,
                Status = SubscriptionStatuses.AwaitingPayment,
                StartDate = today,
                RenewalDate = current.RenewalDate, // Unchanged from the existing cycle.
                AutoRenew = true,
                CreatedAt = utcNow.UtcDateTime
            };
            await subscriptions.AddAsync(pendingSubscription, cancellationToken: cancellationToken);

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                SubscriptionId = pendingSubscription.Id,
                UserId = userId,
                InvoiceNumber = GenerateInvoiceNumber(utcNow),
                InvoiceDate = today,
                DueDate = today,
                // chk_invoices_total requires TotalAmount == AmountUsd + TaxAmount; the invoiced
                // amount is what's actually charged today (post-credit), not the plan's full
                // price — that full price and the credit are still visible in Items above.
                AmountUsd = dueToday,
                TaxAmount = 0m,
                TotalAmount = dueToday,
                Status = InvoiceStatuses.AwaitingPayment,
                PayosOrderCode = orderCode,
                PayosPaymentLinkId = paymentLink.PaymentLinkId,
                PayosQrCode = paymentLink.QrCode,
                Items = itemsJson,
                CreatedAt = utcNow.UtcDateTime
            };
            await invoices.AddAsync(invoice, cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success(new UpgradeResponseDto(
                PaymentRequired: true,
                DueTodayUsd: dueToday,
                ProratedCreditUsd: creditApplied,
                MonthlyRate: newPlanPrice,
                FirstRenewalDate: current.RenewalDate,
                InvoiceId: invoice.Id,
                QrCode: paymentLink.QrCode,
                CheckoutUrl: paymentLink.CheckoutUrl,
                AmountVnd: amountVnd,
                DueTodayVnd: settings.ToVnd(dueToday),
                ProratedCreditVnd: settings.ToVnd(creditApplied),
                MonthlyRateVnd: settings.ToVnd(newPlanPrice)));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Result<DowngradeResponseDto>> DowngradeAsync(
        DowngradeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await downgradeValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<DowngradeResponseDto>(validation.ToValidationError());
        }

        if (!TryGetUserId(out var userId))
        {
            return Result.Failure<DowngradeResponseDto>(SubscriptionErrors.Unauthenticated());
        }

        var current = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);

        // Downgrading requires an active paid plan. A past-due Seller can still pick a cheaper
        // plan: the renewal they pay next is then priced at it.
        if (!IsPaidPlan(current))
        {
            return Result.Failure<DowngradeResponseDto>(SubscriptionErrors.NoActivePlanToChange());
        }

        var targetPlan = await plans.GetPurchasableByIdAsync(request.PlanId, cancellationToken);
        if (targetPlan is null)
        {
            return Result.Failure<DowngradeResponseDto>(SubscriptionErrors.PlanNotFound());
        }

        // The target must be a strictly lower tier than the current plan.
        if (targetPlan.MonthlyPriceUsd >= current!.Plan.MonthlyPriceUsd)
        {
            return Result.Failure<DowngradeResponseDto>(SubscriptionErrors.TargetNotLowerTier());
        }

        // Scheduled for the start of the next cycle; a new selection replaces any previously scheduled downgrade.
        // No payment, no proration: the current plan and its limits stay untouched until the effective date.
        current.ScheduledPlanId = targetPlan.Id;
        current.ScheduledPlan = targetPlan;
        current.ScheduledPlanEffectiveDate = current.RenewalDate;
        current.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await subscriptions.UpdateAsync(current, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new DowngradeResponseDto(
            targetPlan.Id, targetPlan.Name, current.ScheduledPlanEffectiveDate.Value));
    }

    /// <inheritdoc />
    public async Task<Result> CancelScheduledDowngradeAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Result.Failure(SubscriptionErrors.Unauthenticated());
        }

        var current = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);

        // Nothing to cancel once the renewal that applied the scheduled plan was paid
        // (ScheduledPlanId is cleared then).
        if (current is null || current.ScheduledPlanId is null)
        {
            return Result.Failure(SubscriptionErrors.NoScheduledDowngrade());
        }

        current.ScheduledPlanId = null;
        current.ScheduledPlan = null;
        current.ScheduledPlanEffectiveDate = null;
        current.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await subscriptions.UpdateAsync(current, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceFileDto>> DownloadInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Result.Failure<InvoiceFileDto>(SubscriptionErrors.Unauthenticated());
        }

        var invoice = await invoices.GetByIdForUserAsync(invoiceId, userId, cancellationToken);
        if (invoice is null)
        {
            // Invoices stay scoped to the requesting Seller's own invoices.
            return Result.Failure<InvoiceFileDto>(SubscriptionErrors.InvoicePdfNotFound());
        }

        var billingPeriodLabel =
            $"{(invoice.Subscription.BillingCycle == "annual" ? "Annual" : "Monthly")} subscription · {invoice.InvoiceDate:MMMM yyyy}";

        var model = new InvoicePdfModel(
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            billingPeriodLabel,
            invoice.User.FullName,
            invoice.User.Email,
            invoice.Subscription.Plan.Name,
            gatewaySettings.Value.ToVnd(invoice.AmountUsd),
            gatewaySettings.Value.ToVnd(invoice.TotalAmount),
            ToCheckoutStatusValue(invoice));

        byte[] pdfBytes;
        try
        {
            // Read/export-only — nothing about the invoice is written here.
            pdfBytes = invoicePdfRenderer.Render(model);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<InvoiceFileDto>(SubscriptionErrors.InvoicePdfGenerationFailed());
        }

        return Result.Success(new InvoiceFileDto(pdfBytes, $"{invoice.InvoiceNumber}.pdf"));
    }

    /// <summary>
    /// The plan, billing cycle and price of the Seller's next cycle. A scheduled change (a
    /// downgrade, or a retired plan's move) takes effect with it; staying on the same plan keeps
    /// the price the Seller signed up at.
    /// </summary>
    private static (SubscriptionPlan Plan, string BillingCycle, decimal Price) PriceNextCycle(Subscription current)
    {
        var plan = current.ScheduledPlan ?? current.Plan;
        var samePlan = plan.Id == current.PlanId;
        var monthly = samePlan && current.MonthlyPriceUsd > 0m ? current.MonthlyPriceUsd : plan.MonthlyPriceUsd;
        var annual = samePlan && current.AnnualPriceUsd > 0m ? current.AnnualPriceUsd : plan.AnnualPriceUsd;

        // A plan with no annual price renews monthly.
        return current.BillingCycle == "annual" && annual is { } annualPrice
            ? (plan, "annual", annualPrice)
            : (plan, "monthly", monthly);
    }

    /// <summary>
    /// Activates, declines, or cancels a pending checkout. Shared by the webhook handler, the
    /// status-poll reconciliation path, and the explicit cancel endpoint so all three can never
    /// apply different rules.
    /// </summary>
    private async Task FinalizeCheckoutAsync(Invoice invoice, CheckoutOutcome outcome, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var succeeded = outcome == CheckoutOutcome.Paid;

        if (RenewalInvoiceItem.TryRead(invoice.Items) is { } renewal)
        {
            await FinalizeRenewalAsync(invoice, renewal, succeeded, today, cancellationToken);
            return;
        }

        if (succeeded)
        {
            // An Upgrade leaves the Seller's previous plan Active while the new plan's payment is
            // pending (unlike Buy, which cancels the old one immediately at checkout) so the
            // Seller keeps full access if the payment never completes. Now that payment
            // succeeded there must be only one Active subscription, so the old one — if this
            // wasn't already it — is retired here. A no-op for Buy, which already left none.
            var previousActive = await subscriptions.GetActiveWithPlanAsync(invoice.UserId, cancellationToken);
            if (previousActive is not null && previousActive.Id != invoice.SubscriptionId)
            {
                previousActive.Status = SubscriptionStatuses.Cancelled;
                previousActive.CancelledAt = timeProvider.GetUtcNow().UtcDateTime;
                await subscriptions.UpdateAsync(previousActive, cancellationToken: cancellationToken);
            }

            await CloseOpenRenewalsAsync(invoice, cancellationToken);
        }

        invoice.Status = succeeded ? InvoiceStatuses.Paid : InvoiceStatuses.Void;
        if (succeeded)
        {
            invoice.PaymentDate = today;
        }

        // Declined and Cancelled both leave the invoice "void" (the DB has no distinct value for
        // either), but the subscription status keeps them apart — Expired vs. Cancelled — so the
        // API can still tell the Seller which one actually happened (see ToCheckoutStatusValue).
        invoice.Subscription.Status = outcome switch
        {
            CheckoutOutcome.Paid => SubscriptionStatuses.Active,
            CheckoutOutcome.Cancelled => SubscriptionStatuses.Cancelled,
            CheckoutOutcome.Declined => SubscriptionStatuses.Expired,
            _ => invoice.Subscription.Status
        };
        if (!succeeded)
        {
            invoice.Subscription.CancelledAt = timeProvider.GetUtcNow().UtcDateTime;
        }

        await invoices.UpdateAsync(invoice, cancellationToken: cancellationToken);
        await subscriptions.UpdateAsync(invoice.Subscription, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private enum PaymentLinkClosure
    {
        /// <summary>The link can no longer be paid.</summary>
        Closed,

        /// <summary>The Seller paid before the link could be closed.</summary>
        Paid,

        /// <summary>PayOS could not say: the link may still be paid, and its webhook settles it.</summary>
        StillOpen
    }

    /// <summary>
    /// Closes a payment link, asking PayOS for its status first: a link that may already be paid
    /// is never cancelled on a guess. A refused cancel is checked the same way, since the Seller
    /// can pay between the two calls.
    /// </summary>
    private async Task<PaymentLinkClosure> ClosePaymentLinkAsync(
        long orderCode,
        string reason,
        CancellationToken cancellationToken)
    {
        var status = await TryReadLinkAsync(orderCode, cancellationToken);
        if (status is not { IsPaid: false, IsFailed: false })
        {
            return ToClosure(status);
        }

        try
        {
            await paymentGateway.CancelPaymentLinkAsync(orderCode, reason, cancellationToken);
            return PaymentLinkClosure.Closed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ToClosure(await TryReadLinkAsync(orderCode, cancellationToken));
        }
    }

    private static PaymentLinkClosure ToClosure(PaymentLinkStatusResult? status) =>
        status is null ? PaymentLinkClosure.StillOpen
        : status.IsPaid ? PaymentLinkClosure.Paid
        : status.IsFailed ? PaymentLinkClosure.Closed
        : PaymentLinkClosure.StillOpen;

    private async Task<PaymentLinkStatusResult?> TryReadLinkAsync(long orderCode, CancellationToken cancellationToken)
    {
        try
        {
            return await paymentGateway.GetPaymentLinkStatusAsync(orderCode, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// A new plan replaces the old one, so a renewal link still open for the old plan must not be
    /// payable afterwards: that money would buy nothing. A link that cannot be closed stays as it is.
    /// </summary>
    private async Task CloseOpenRenewalsAsync(Invoice paidInvoice, CancellationToken cancellationToken)
    {
        var open = await invoices.GetByStatusForUserAsync(paidInvoice.UserId, InvoiceStatuses.AwaitingPayment, cancellationToken);
        foreach (var renewal in open.Where(candidate => candidate.Id != paidInvoice.Id
            && candidate.SubscriptionId != paidInvoice.SubscriptionId
            && RenewalInvoiceItem.TryRead(candidate.Items) is not null))
        {
            var closure = renewal.PayosOrderCode is { } orderCode
                ? await ClosePaymentLinkAsync(orderCode, "Replaced by a new plan", cancellationToken)
                : PaymentLinkClosure.Closed;
            if (closure == PaymentLinkClosure.Closed)
            {
                renewal.Status = InvoiceStatuses.Void;
                await invoices.UpdateAsync(renewal, cancellationToken: cancellationToken);
            }
        }
    }

    /// <summary>
    /// Settles a renewal invoice. Paid: the subscription gets one more cycle of the plan that was
    /// priced. Not paid: only the invoice is voided; the subscription keeps whatever time it has.
    /// </summary>
    private async Task FinalizeRenewalAsync(
        Invoice invoice,
        RenewalInvoiceItem renewal,
        bool succeeded,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        invoice.Status = succeeded ? InvoiceStatuses.Paid : InvoiceStatuses.Void;
        if (succeeded)
        {
            invoice.PaymentDate = today;
            var subscription = invoice.Subscription;
            var stillUsable = subscription.Status is SubscriptionStatuses.Active or SubscriptionStatuses.PastDue
                && subscription.RenewalDate >= RenewalRules.OldestUsableRenewalDate(today);

            // Paid after the grace period ended: the plan comes back from today, unless the Seller
            // already bought another one meanwhile, which a second live plan would contradict.
            var other = stillUsable ? null : await subscriptions.GetActiveWithPlanAsync(invoice.UserId, cancellationToken);
            if (other is null || other.Id == subscription.Id)
            {
                await ApplyRenewalAsync(subscription, renewal, stillUsable ? subscription.RenewalDate : today, cancellationToken);
            }
            else
            {
                // Should not happen: buying the other plan closes this link. If the payment still
                // got through, nothing was delivered for it, so the money has to be returned by hand.
                logger.LogError(
                    "Renewal invoice {InvoiceId} was paid but not applied: user {UserId} is on subscription {SubscriptionId}. Refund it by hand.",
                    invoice.Id, invoice.UserId, other.Id);
            }
        }

        await invoices.UpdateAsync(invoice, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyRenewalAsync(
        Subscription subscription,
        RenewalInvoiceItem renewal,
        DateOnly periodStart,
        CancellationToken cancellationToken)
    {
        if (renewal.PlanId != subscription.PlanId)
        {
            var plan = await plans.GetByIdForAdminAsync(renewal.PlanId, cancellationToken) ?? subscription.Plan;
            subscription.PlanId = plan.Id;
            subscription.Plan = plan;
            subscription.MonthlyPriceUsd = plan.MonthlyPriceUsd;
            subscription.AnnualPriceUsd = plan.AnnualPriceUsd;
        }

        // The scheduled change is used up once the renewal that carried it is paid. One picked
        // while the payment was open still waits for the next renewal.
        if (subscription.ScheduledPlanId == renewal.PlanId)
        {
            subscription.ScheduledPlanId = null;
            subscription.ScheduledPlan = null;
            subscription.ScheduledPlanEffectiveDate = null;
        }

        subscription.BillingCycle = renewal.BillingCycle;
        subscription.StartDate = periodStart;
        subscription.RenewalDate = RenewalRules.NextRenewalDate(periodStart, renewal.BillingCycle);
        subscription.Status = SubscriptionStatuses.Active;
        subscription.CancelledAt = null;
        subscription.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await subscriptions.UpdateAsync(subscription, cancellationToken: cancellationToken);
    }

    private bool TryGetUserId(out Guid userId)
    {
        if (currentUser.IsAuthenticated && currentUser.UserId is { } id)
        {
            userId = id;
            return true;
        }

        userId = Guid.Empty;
        return false;
    }

    // A trial runs on a paid plan's features but was never paid for, so the Seller buys rather
    // than upgrades out of it. A past-due plan is still the Seller's plan: they renew it.
    private static bool IsPaidPlan(Subscription? subscription) =>
        subscription is not null
        && subscription.Status is SubscriptionStatuses.Active or SubscriptionStatuses.PastDue
        && subscription.TrialEndsAt is null
        && subscription.Plan.MonthlyPriceUsd > FreeTierMaxPrice;

    private static string GenerateInvoiceNumber(DateTimeOffset utcNow) =>
        $"INV-{utcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    /// <summary>
    /// Translates the database's real invoice/subscription statuses into the simplified
    /// pending/paid/failed/cancelled vocabulary the API exposes to the client. Both a PayOS
    /// decline and a Seller-initiated cancel leave the invoice "void" (the DB has no separate
    /// value for either) — the linked subscription's status (Expired vs. Cancelled) is what
    /// actually distinguishes them; see <see cref="FinalizeCheckoutAsync"/>.
    /// </summary>
    private static string ToCheckoutStatusValue(Invoice invoice)
    {
        if (invoice.Status == InvoiceStatuses.Paid)
        {
            return CheckoutStatusValues.Paid;
        }

        if (invoice.Status == InvoiceStatuses.Void)
        {
            // An unpaid renewal never touches its subscription, so its status cannot tell a decline
            // from a cancel; either way the Seller simply did not renew.
            if (RenewalInvoiceItem.TryRead(invoice.Items) is not null)
            {
                return CheckoutStatusValues.Cancelled;
            }

            return invoice.Subscription.Status == SubscriptionStatuses.Cancelled
                ? CheckoutStatusValues.Cancelled
                : CheckoutStatusValues.Failed;
        }

        return CheckoutStatusValues.Pending;
    }

    private static CheckoutStatusResponseDto MapToStatusDto(Invoice invoice)
    {
        var status = ToCheckoutStatusValue(invoice);
        return new CheckoutStatusResponseDto(
            status,
            invoice.Subscription.Plan?.Name ?? string.Empty,
            invoice.InvoiceNumber,
            status == CheckoutStatusValues.Paid ? invoice.Subscription.RenewalDate : null);
    }

    private static CurrentSubscriptionDto MapToCurrentSubscriptionDto(
        Subscription subscription,
        DateOnly today,
        PaymentGatewaySettings settings)
    {
        var isTrial = subscription.TrialEndsAt is not null;
        var isPaid = IsPaidPlan(subscription);
        var pastDue = isPaid && RenewalRules.IsPastDue(subscription.RenewalDate, today);
        var nextCycle = isPaid ? PriceNextCycle(subscription) : default;
        var price = subscription.BillingCycle == "annual" && subscription.AnnualPriceUsd is { } annualPrice
            ? annualPrice
            : subscription.MonthlyPriceUsd;

        return new(
            subscription.Id,
            subscription.PlanId,
            subscription.Plan.Name,
            subscription.Plan.Description,
            // The hourly job may not have stored it yet.
            pastDue ? SubscriptionStatuses.PastDue : subscription.Status,
            subscription.BillingCycle,
            price,
            subscription.StartDate,
            subscription.RenewalDate,
            subscription.ScheduledPlan?.Name,
            subscription.ScheduledPlanEffectiveDate,
            isTrial,
            subscription.TrialEndsAt,
            CanRenew: isPaid && RenewalRules.CanRenew(subscription.RenewalDate, today),
            LastGraceDay: pastDue ? RenewalRules.LastGraceDay(subscription.RenewalDate) : null,
            RenewalPlanName: isPaid ? nextCycle.Plan.Name : null,
            RenewalPrice: isPaid ? nextCycle.Price : null,
            PriceVnd: settings.ToVnd(price),
            RenewalPriceVnd: isPaid ? settings.ToVnd(nextCycle.Price) : null);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FeatureCatalogItemDto>>> GetFeatureCatalogAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await featureCatalog.GetAllAsync(cancellationToken);
        return Result.Success<IReadOnlyList<FeatureCatalogItemDto>>(catalog
            .Select(feature => new FeatureCatalogItemDto(
                feature.Code,
                feature.Label,
                feature.Description,
                feature.Group,
                feature.Kind,
                feature.Unit,
                feature.SortOrder))
            .ToList());
    }

    private static AvailablePlanDto MapToAvailablePlanDto(
        SubscriptionPlan plan,
        Guid? currentPlanId,
        PaymentGatewaySettings settings) =>
        new(
            plan.Id,
            plan.Name,
            plan.Tier,
            plan.Description,
            plan.MonthlyPriceUsd,
            plan.AnnualPriceUsd,
            plan.Id == currentPlanId,
            plan.PlanFeatures
                .Select(feature => new PlanFeatureFlagDto(
                    feature.FeatureCode,
                    feature.IsEnabled ?? false,
                    feature.LimitValue))
                .ToList(),
            new PlanQuotasDto(
                plan.ImageGenerationQuota,
                plan.VideoGenerationQuota,
                plan.ApiCallQuota,
                plan.StorageQuotaGb,
                plan.MaxBatchSize,
                plan.MaxProductsPerMonth,
                plan.MaxConcurrentJobs),
            settings.ToVnd(plan.MonthlyPriceUsd),
            plan.AnnualPriceUsd is { } annual ? settings.ToVnd(annual) : null);

    private static RecentInvoiceDto MapToRecentInvoiceDto(Invoice invoice, PaymentGatewaySettings settings) =>
        new(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.Subscription.Plan.Name,
            invoice.TotalAmount,
            ToCheckoutStatusValue(invoice),
            settings.ToVnd(invoice.TotalAmount));
}
