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
    IOptions<PaymentGatewaySettings> gatewaySettings,
    TimeProvider timeProvider,
    IValidator<CheckoutRequestDto> checkoutValidator)
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

    /// <summary>
    /// Activates, declines, or cancels a pending checkout. Shared by the webhook handler, the
    /// status-poll reconciliation path, and the explicit cancel endpoint so all three can never
    /// apply different rules.
    /// </summary>
    private async Task FinalizeCheckoutAsync(Invoice invoice, CheckoutOutcome outcome, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var succeeded = outcome == CheckoutOutcome.Paid;

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
            subscription.RenewalDate);

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
