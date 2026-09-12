using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.TestSupport;

internal static class SubscriptionTestData
{
    public static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly DateTimeOffset UtcNow = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    public static readonly Guid FreePlanId = Guid.Parse("33333333-3333-3333-3333-333333333331");
    public static readonly Guid StarterPlanId = Guid.Parse("33333333-3333-3333-3333-333333333332");
    public static readonly Guid ProPlanId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static SubscriptionPlan CreateFreePlan() => new()
    {
        Id = FreePlanId,
        Name = "Free",
        Tier = "free",
        Description = "Free tier",
        MonthlyPriceUsd = 0m,
        AnnualPriceUsd = null,
        MaxBatchSize = 10,
        MaxProductsPerMonth = 10,
        MaxConcurrentJobs = 1,
        ImageGenerationQuota = 20,
        VideoGenerationQuota = 0,
        ApiCallQuota = 100,
        StorageQuotaGb = 1m,
        IsActive = true,
        SortOrder = 0,
        PlanFeatures = []
    };

    /// <summary>
    /// A paid plan offering both Monthly and Annual billing.
    /// </summary>
    public static SubscriptionPlan CreateStarterPlan() => new()
    {
        Id = StarterPlanId,
        Name = "Starter",
        Tier = "starter",
        Description = "Starter tier",
        MonthlyPriceUsd = 9.99m,
        AnnualPriceUsd = 99.99m,
        MaxBatchSize = 50,
        MaxProductsPerMonth = 200,
        MaxConcurrentJobs = 2,
        ImageGenerationQuota = 500,
        VideoGenerationQuota = 10,
        ApiCallQuota = 5000,
        StorageQuotaGb = 10m,
        IsActive = true,
        SortOrder = 1,
        PlanFeatures = []
    };

    /// <summary>
    /// A paid plan with no annual price, to exercise BR191's "annual unavailable" branch.
    /// </summary>
    public static SubscriptionPlan CreateProPlan() => new()
    {
        Id = ProPlanId,
        Name = "Pro",
        Tier = "pro",
        Description = "Pro tier",
        MonthlyPriceUsd = 29.99m,
        AnnualPriceUsd = null,
        MaxBatchSize = 200,
        MaxProductsPerMonth = 1000,
        MaxConcurrentJobs = 5,
        ImageGenerationQuota = 2000,
        VideoGenerationQuota = 50,
        ApiCallQuota = 20000,
        StorageQuotaGb = 50m,
        IsActive = true,
        SortOrder = 2,
        PlanFeatures = []
    };

    public static Subscription CreateActiveSubscription(SubscriptionPlan plan, string billingCycle = "monthly") => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        PlanId = plan.Id,
        Plan = plan,
        BillingCycle = billingCycle,
        MonthlyPriceUsd = plan.MonthlyPriceUsd,
        AnnualPriceUsd = plan.AnnualPriceUsd,
        Status = "active",
        StartDate = DateOnly.FromDateTime(UtcNow.UtcDateTime),
        RenewalDate = DateOnly.FromDateTime(UtcNow.UtcDateTime).AddMonths(1),
        AutoRenew = true,
        CreatedAt = UtcNow.UtcDateTime
    };

    public static Invoice CreatePendingInvoice(Subscription subscription, long orderCode) => new()
    {
        Id = Guid.NewGuid(),
        SubscriptionId = subscription.Id,
        Subscription = subscription,
        UserId = UserId,
        InvoiceNumber = "INV-TEST-0001",
        InvoiceDate = DateOnly.FromDateTime(UtcNow.UtcDateTime),
        DueDate = DateOnly.FromDateTime(UtcNow.UtcDateTime),
        AmountUsd = subscription.MonthlyPriceUsd,
        TaxAmount = 0m,
        TotalAmount = subscription.MonthlyPriceUsd,
        Status = "issued",
        PayosOrderCode = orderCode,
        PayosPaymentLinkId = "test-payment-link-id",
        PayosQrCode = "test-qr-code",
        Items = "[]",
        CreatedAt = UtcNow.UtcDateTime
    };

    public static FakeTimeProvider CreateTimeProvider() => new(UtcNow);

    public static Mock<ICurrentUser> CreateAuthenticatedUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.IsAuthenticated).Returns(true);
        currentUser.Setup(candidate => candidate.UserId).Returns(UserId);
        return currentUser;
    }

    public static Mock<IUnitOfWorkTransaction> CreateTransaction()
    {
        var transaction = new Mock<IUnitOfWorkTransaction>();
        transaction.Setup(candidate => candidate.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return transaction;
    }

    /// <summary>
    /// Creates a validator mock that reports every request as valid, so tests that exercise
    /// <see cref="SubscriptionService"/> business logic are not also exercising FluentValidation
    /// rules covered separately by the feature's validator tests.
    /// </summary>
    public static Mock<IValidator<T>> CreatePassingValidator<T>()
    {
        var validator = new Mock<IValidator<T>>();
        validator.Setup(candidate => candidate.ValidateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        return validator;
    }

    public static IOptions<PaymentGatewaySettings> CreateGatewaySettings(
        decimal usdToVndRate = 25000m, int? testAmountVnd = 2000) =>
        Options.Create(new PaymentGatewaySettings(usdToVndRate, testAmountVnd));

    /// <summary>
    /// Builds a <see cref="SubscriptionService"/> with loose mocks for every dependency a test
    /// does not override, including a passing validator and an authenticated current user.
    /// </summary>
    public static SubscriptionService CreateService(
        Mock<ISubscriptionRepository>? subscriptions = null,
        Mock<IPlanRepository>? plans = null,
        Mock<IInvoiceRepository>? invoices = null,
        Mock<IUsageStatisticRepository>? usageStatistics = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<ICurrentUser>? currentUser = null,
        Mock<IPaymentGatewayClient>? paymentGateway = null,
        IOptions<PaymentGatewaySettings>? gatewaySettings = null,
        TimeProvider? timeProvider = null,
        Mock<IValidator<CheckoutRequestDto>>? checkoutValidator = null) =>
        new(
            (subscriptions ?? new Mock<ISubscriptionRepository>()).Object,
            (plans ?? new Mock<IPlanRepository>()).Object,
            (invoices ?? new Mock<IInvoiceRepository>()).Object,
            (usageStatistics ?? new Mock<IUsageStatisticRepository>()).Object,
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            (currentUser ?? CreateAuthenticatedUser()).Object,
            (paymentGateway ?? DefaultApprovingGateway()).Object,
            gatewaySettings ?? CreateGatewaySettings(),
            timeProvider ?? CreateTimeProvider(),
            (checkoutValidator ?? CreatePassingValidator<CheckoutRequestDto>()).Object);

    private static Mock<IPaymentGatewayClient> DefaultApprovingGateway()
    {
        var gateway = new Mock<IPaymentGatewayClient>();
        gateway.Setup(candidate => candidate.CreatePaymentLinkAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLinkResult("test-payment-link-id", "https://payos.vn/checkout/test", "test-qr-code"));
        return gateway;
    }
}
