using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.Features.Subscriptions.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Subscriptions;

/// <summary>
/// Configuration <see cref="SubscriptionService"/> needs for real PayOS checkouts.
/// </summary>
/// <remarks>
/// A narrow Application-owned projection of <c>PayOsOptions</c> (Infrastructure), so Application
/// never references the Infrastructure options type directly.
/// </remarks>
public sealed record PaymentGatewaySettings(decimal UsdToVndRate, int? TestAmountVnd);

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
    IValidator<DowngradeRequestDto> downgradeValidator)
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

        var activeSubscription = await GetActiveSubscriptionApplyingDueDowngradeAsync(userId, cancellationToken);
        var hasActivePaidPlan = IsActivePaidPlan(activeSubscription);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var usage = activeSubscription is null
            ? null
            : await usageStatistics.GetCurrentPeriodAsync(userId, today, cancellationToken);

        var usageQuotas = activeSubscription is null
            ? []
            : QuotaDefinitions.Build(activeSubscription.Plan, usage);

        var comparisonPlans = await plans.GetComparisonPlansAsync(activeSubscription?.PlanId, cancellationToken);
        var availablePlans = comparisonPlans
            .Select(plan => MapToAvailablePlanDto(plan, activeSubscription?.PlanId))
            .ToList();

        var recentInvoices = await invoices.GetRecentWithPlanAsync(userId, take: 3, cancellationToken);
        var recentInvoiceDtos = recentInvoices.Select(MapToRecentInvoiceDto).ToList();

        var response = new SubscriptionOverviewResponseDto(
            activeSubscription is null ? null : MapToCurrentSubscriptionDto(activeSubscription),
            usageQuotas,
            availablePlans,
            recentInvoiceDtos,
            hasActivePaidPlan);

        return Result.Success(response);
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
        // Seller already on a paid plan must use Upgrade/Downgrade (out of this scope).
        if (IsActivePaidPlan(activeSubscription))
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
            if (activeSubscription is not null)
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

        if (invoice.PayosOrderCode is not null)
        {
            try
            {
                await paymentGateway.CancelPaymentLinkAsync(
                    invoice.PayosOrderCode.Value, "Seller cancelled checkout", cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Best-effort: PayOS may already consider the link expired/gone. The local rows
                // are still marked cancelled below regardless, so the Seller is never blocked.
            }
        }

        await FinalizeCheckoutAsync(invoice, CheckoutOutcome.Cancelled, cancellationToken);
        return Result.Success();
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

        var current = await GetActiveSubscriptionApplyingDueDowngradeAsync(userId, cancellationToken);

        // Upgrading requires an active paid plan.
        if (!IsActivePaidPlan(current))
        {
            return Result.Failure<UpgradeResponseDto>(SubscriptionErrors.NoActivePlanToChange());
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
                AmountVnd: null));
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
                AmountVnd: amountVnd));
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

        var current = await GetActiveSubscriptionApplyingDueDowngradeAsync(userId, cancellationToken);

        // Downgrading requires an active paid plan.
        if (!IsActivePaidPlan(current))
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

        var current = await GetActiveSubscriptionApplyingDueDowngradeAsync(userId, cancellationToken);

        // Nothing to cancel once the effective date already passed and the lazy-apply already
        // promoted the scheduled plan (ScheduledPlanId is cleared).
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
            invoice.AmountUsd,
            invoice.TotalAmount,
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
    /// Gets the Seller's active subscription, applying a scheduled downgrade in place first if
    /// its effective date has already arrived. There is no background job scheduler in
    /// this project, so the transition is applied lazily the next time anything reads the
    /// subscription instead of exactly at midnight on the effective date.
    /// </summary>
    private async Task<Subscription?> GetActiveSubscriptionApplyingDueDowngradeAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
        if (subscription?.ScheduledPlanId is null)
        {
            return subscription;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (subscription.ScheduledPlanEffectiveDate > today)
        {
            return subscription;
        }

        var newPlan = subscription.ScheduledPlan
            ?? await plans.GetPurchasableByIdAsync(subscription.ScheduledPlanId.Value, cancellationToken)
            ?? subscription.Plan;
        var utcNow = timeProvider.GetUtcNow();

        subscription.StartDate = subscription.RenewalDate;
        subscription.RenewalDate = subscription.BillingCycle == "annual"
            ? subscription.RenewalDate.AddYears(1)
            : subscription.RenewalDate.AddMonths(1);
        subscription.PlanId = newPlan.Id;
        subscription.Plan = newPlan;
        subscription.MonthlyPriceUsd = newPlan.MonthlyPriceUsd;
        subscription.AnnualPriceUsd = newPlan.AnnualPriceUsd;
        subscription.ScheduledPlanId = null;
        subscription.ScheduledPlan = null;
        subscription.ScheduledPlanEffectiveDate = null;
        subscription.UpdatedAt = utcNow.UtcDateTime;

        await subscriptions.UpdateAsync(subscription, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return subscription;
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

    private static bool IsActivePaidPlan(Subscription? subscription) =>
        subscription is not null
        && subscription.Status == SubscriptionStatuses.Active
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

    private static CurrentSubscriptionDto MapToCurrentSubscriptionDto(Subscription subscription) =>
        new(
            subscription.Id,
            subscription.PlanId,
            subscription.Plan.Name,
            subscription.Plan.Description,
            subscription.Status,
            subscription.BillingCycle,
            subscription.BillingCycle == "annual" && subscription.AnnualPriceUsd is { } annualPrice
                ? annualPrice
                : subscription.MonthlyPriceUsd,
            subscription.StartDate,
            subscription.RenewalDate,
            subscription.ScheduledPlan?.Name,
            subscription.ScheduledPlanEffectiveDate);

    private static AvailablePlanDto MapToAvailablePlanDto(SubscriptionPlan plan, Guid? currentPlanId) =>
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
                plan.MaxConcurrentJobs));

    private static RecentInvoiceDto MapToRecentInvoiceDto(Invoice invoice) =>
        new(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.Subscription.Plan.Name,
            invoice.TotalAmount,
            ToCheckoutStatusValue(invoice));
}
