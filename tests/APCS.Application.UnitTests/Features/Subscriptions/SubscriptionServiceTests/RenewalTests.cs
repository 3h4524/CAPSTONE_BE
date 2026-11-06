using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Subscriptions.SubscriptionServiceTests;

[TestClass]
public sealed class RenewalTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime);

    private static Mock<ISubscriptionRepository> CurrentIs(Subscription? current)
    {
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        return subscriptions;
    }

    private static Subscription PastDue(SubscriptionPlan plan, int daysOverdue = 1)
    {
        var subscription = SubscriptionTestData.CreateActiveSubscription(plan);
        subscription.Status = "past_due";
        subscription.RenewalDate = Today.AddDays(-daysOverdue);
        subscription.StartDate = subscription.RenewalDate.AddMonths(-1);
        return subscription;
    }

    private static Invoice RenewalInvoice(Subscription subscription, SubscriptionPlan plan, string billingCycle = "monthly")
    {
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.Items = new RenewalInvoiceItem("renewal", 2000, plan.Id, billingCycle).ToItemsJson();
        return invoice;
    }

    private static Mock<IPaymentGatewayClient> WebhookSays(bool success)
    {
        var gateway = new Mock<IPaymentGatewayClient>();
        gateway.Setup(candidate => candidate.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(true, 123, success));
        return gateway;
    }

    private static SubscriptionService OverviewService(Subscription current)
    {
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetComparisonPlansAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return SubscriptionTestData.CreateService(CurrentIs(current), plans, invoices);
    }

    private static Mock<IInvoiceRepository> InvoiceIs(Invoice invoice)
    {
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByPayosOrderCodeAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        return invoices;
    }

    [TestMethod]
    public async Task RenewAsync_WithoutAPaidPlan_ReturnsNothingToRenew()
    {
        var service = SubscriptionTestData.CreateService(CurrentIs(null));

        var result = await service.RenewAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionNothingToRenew);
    }

    [TestMethod]
    public async Task RenewAsync_LongBeforeTheRenewalDate_ReturnsRenewalNotDueAndChargesNothing()
    {
        var current = SubscriptionTestData.CreateActiveSubscription(SubscriptionTestData.CreateStarterPlan());
        var gateway = new Mock<IPaymentGatewayClient>();
        var service = SubscriptionTestData.CreateService(CurrentIs(current), paymentGateway: gateway);

        var result = await service.RenewAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionRenewalNotDue);
        gateway.Verify(
            candidate => candidate.CreatePaymentLinkAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task RenewAsync_WithinAWeekOfTheRenewalDate_IsAllowed()
    {
        var current = SubscriptionTestData.CreateActiveSubscription(SubscriptionTestData.CreateStarterPlan());
        current.RenewalDate = Today.AddDays(RenewalRules.RenewWindowDays);
        var service = SubscriptionTestData.CreateService(CurrentIs(current));

        var result = await service.RenewAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task RenewAsync_WhenPastDue_BillsTheSameSubscriptionAtItsOwnPriceWithoutChangingIt()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var current = PastDue(starter);
        current.MonthlyPriceUsd = 7.99m; // Signed up before a price rise.
        var subscriptions = CurrentIs(current);
        var invoices = new Mock<IInvoiceRepository>();
        var service = SubscriptionTestData.CreateService(subscriptions, invoices: invoices);

        var result = await service.RenewAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AmountVnd.Should().Be(2000, "the dev gateway settings cap every charge");
        invoices.Verify(
            candidate => candidate.AddAsync(
                It.Is<Invoice>(invoice => invoice.SubscriptionId == current.Id
                    && invoice.Status == "issued"
                    && invoice.TotalAmount == 7.99m
                    && invoice.DueDate == current.RenewalDate
                    && RenewalInvoiceItem.TryRead(invoice.Items)!.PlanId == starter.Id),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(It.IsAny<Subscription>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task RenewAsync_WithAScheduledDowngrade_BillsTheScheduledPlan()
    {
        var pro = SubscriptionTestData.CreateProPlan();
        var starter = SubscriptionTestData.CreateStarterPlan();
        var current = PastDue(pro);
        current.ScheduledPlanId = starter.Id;
        current.ScheduledPlan = starter;
        var invoices = new Mock<IInvoiceRepository>();
        var service = SubscriptionTestData.CreateService(CurrentIs(current), invoices: invoices);

        var result = await service.RenewAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoices.Verify(
            candidate => candidate.AddAsync(
                It.Is<Invoice>(invoice => invoice.TotalAmount == starter.MonthlyPriceUsd
                    && RenewalInvoiceItem.TryRead(invoice.Items)!.PlanId == starter.Id),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task RenewAsync_AnnualPlanMovingToAPlanWithoutAnnualBilling_RenewsMonthly()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var pro = SubscriptionTestData.CreateProPlan(); // No annual price.
        var current = SubscriptionTestData.CreateActiveSubscription(starter, billingCycle: "annual");
        current.RenewalDate = Today;
        current.ScheduledPlanId = pro.Id;
        current.ScheduledPlan = pro;
        var invoices = new Mock<IInvoiceRepository>();
        var service = SubscriptionTestData.CreateService(CurrentIs(current), invoices: invoices);

        await service.RenewAsync(CancellationToken.None);

        invoices.Verify(
            candidate => candidate.AddAsync(
                It.Is<Invoice>(invoice => invoice.TotalAmount == pro.MonthlyPriceUsd
                    && RenewalInvoiceItem.TryRead(invoice.Items)!.BillingCycle == "monthly"),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task PaidRenewal_DuringGrace_AddsOneCycleFromTheRenewalDateAndReactivates()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = PastDue(starter, daysOverdue: 2);
        var dueOn = subscription.RenewalDate;
        var invoice = RenewalInvoice(subscription, starter);
        var subscriptions = CurrentIs(subscription);
        var invoices = InvoiceIs(invoice);
        var service = SubscriptionTestData.CreateService(subscriptions, invoices: invoices, paymentGateway: WebhookSays(true));

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        invoice.Status.Should().Be("paid");
        subscription.Status.Should().Be("active");
        subscription.StartDate.Should().Be(dueOn, "the paid cycle starts where the last one ended, not on the payment day");
        subscription.RenewalDate.Should().Be(dueOn.AddMonths(1));
        subscription.PlanId.Should().Be(starter.Id);
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(subscription, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task PaidRenewal_ForAScheduledPlan_SwitchesToItAndClearsTheSchedule()
    {
        var pro = SubscriptionTestData.CreateProPlan();
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = PastDue(pro);
        subscription.ScheduledPlanId = starter.Id;
        subscription.ScheduledPlan = starter;
        subscription.ScheduledPlanEffectiveDate = subscription.RenewalDate;
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetByIdForAdminAsync(starter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(starter);
        var service = SubscriptionTestData.CreateService(
            CurrentIs(subscription), plans, InvoiceIs(RenewalInvoice(subscription, starter)), paymentGateway: WebhookSays(true));

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        subscription.PlanId.Should().Be(starter.Id);
        subscription.MonthlyPriceUsd.Should().Be(starter.MonthlyPriceUsd);
        subscription.ScheduledPlanId.Should().BeNull();
        subscription.ScheduledPlanEffectiveDate.Should().BeNull();
    }

    [TestMethod]
    public async Task PaidRenewal_AfterThePlanExpired_RestartsFromTheDayOfPayment()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = PastDue(starter, daysOverdue: 10);
        subscription.Status = "expired";
        var service = SubscriptionTestData.CreateService(
            CurrentIs(null), invoices: InvoiceIs(RenewalInvoice(subscription, starter)), paymentGateway: WebhookSays(true));

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        subscription.Status.Should().Be("active");
        subscription.StartDate.Should().Be(Today);
        subscription.RenewalDate.Should().Be(Today.AddMonths(1));
        subscription.CancelledAt.Should().BeNull();
    }

    [TestMethod]
    public async Task PaidRenewal_AfterTheSellerBoughtAnotherPlan_LeavesBothPlansAlone()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var expired = PastDue(starter, daysOverdue: 10);
        expired.Status = "expired";
        var newPlan = SubscriptionTestData.CreateActiveSubscription(SubscriptionTestData.CreateProPlan());
        var invoice = RenewalInvoice(expired, starter);
        var subscriptions = CurrentIs(newPlan);
        var service = SubscriptionTestData.CreateService(subscriptions, invoices: InvoiceIs(invoice), paymentGateway: WebhookSays(true));

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        invoice.Status.Should().Be("paid");
        expired.Status.Should().Be("expired");
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(It.IsAny<Subscription>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UnpaidRenewal_VoidsOnlyTheInvoiceAndReadsAsCancelled()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = PastDue(starter);
        var dueOn = subscription.RenewalDate;
        var invoice = RenewalInvoice(subscription, starter);
        var subscriptions = CurrentIs(subscription);
        var invoices = InvoiceIs(invoice);
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var service = SubscriptionTestData.CreateService(subscriptions, invoices: invoices, paymentGateway: WebhookSays(false));

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);
        var status = await service.GetCheckoutStatusAsync(invoice.Id, CancellationToken.None);

        invoice.Status.Should().Be("void");
        subscription.Status.Should().Be("past_due", "a failed renewal payment leaves the grace period running");
        subscription.RenewalDate.Should().Be(dueOn);
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(It.IsAny<Subscription>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        status.Value.Status.Should().Be("cancelled");
    }

    [TestMethod]
    public async Task GetOverviewAsync_WhenPastDue_ShowsPastDueWithTheLastGraceDayAndTheRenewalPrice()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var current = SubscriptionTestData.CreateActiveSubscription(starter);
        current.RenewalDate = Today; // Stored "active": the hourly job has not run yet.
        var service = OverviewService(current);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        var dto = result.Value.CurrentSubscription!;
        dto.Status.Should().Be("past_due");
        dto.CanRenew.Should().BeTrue();
        dto.LastGraceDay.Should().Be(Today.AddDays(RenewalRules.GraceDays - 1));
        dto.RenewalPlanName.Should().Be(starter.Name);
        dto.RenewalPrice.Should().Be(starter.MonthlyPriceUsd);
        result.Value.HasActivePaidPlan.Should().BeTrue("a past-due Seller renews rather than buys");
    }

    [TestMethod]
    public async Task GetOverviewAsync_EarlyInTheCycle_CannotRenewYet()
    {
        var current = SubscriptionTestData.CreateActiveSubscription(SubscriptionTestData.CreateStarterPlan());
        var service = OverviewService(current);

        var dto = (await service.GetOverviewAsync(CancellationToken.None)).Value.CurrentSubscription!;

        dto.Status.Should().Be("active");
        dto.CanRenew.Should().BeFalse();
        dto.LastGraceDay.Should().BeNull();
    }

    private static (SubscriptionService Service, Invoice OpenRenewal, Mock<IPaymentGatewayClient> Gateway) UpgradePaidWithAnOpenRenewal()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var oldPlan = SubscriptionTestData.CreateActiveSubscription(starter);
        var newPlan = SubscriptionTestData.CreateActiveSubscription(SubscriptionTestData.CreateProPlan());
        newPlan.Status = "trialing";
        var upgrade = SubscriptionTestData.CreatePendingInvoice(newPlan, 123);
        var openRenewal = RenewalInvoice(oldPlan, starter);
        openRenewal.PayosOrderCode = 555;

        var invoices = InvoiceIs(upgrade);
        invoices.Setup(candidate => candidate.GetByStatusForUserAsync(SubscriptionTestData.UserId, "issued", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { openRenewal, upgrade });
        var gateway = WebhookSays(true);
        gateway.Setup(candidate => candidate.GetPaymentLinkStatusAsync(555, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLinkStatusResult(false, false));
        return (SubscriptionTestData.CreateService(CurrentIs(oldPlan), invoices: invoices, paymentGateway: gateway), openRenewal, gateway);
    }

    [TestMethod]
    public async Task PaidUpgrade_ClosesARenewalLinkStillOpenForTheOldPlan()
    {
        var (service, openRenewal, gateway) = UpgradePaidWithAnOpenRenewal();

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        gateway.Verify(candidate => candidate.CancelPaymentLinkAsync(555, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        openRenewal.Status.Should().Be("void", "paying it now would buy nothing");
    }

    [TestMethod]
    public async Task PaidUpgrade_LeavesARenewalLinkThatMayStillBePaid()
    {
        var (service, openRenewal, gateway) = UpgradePaidWithAnOpenRenewal();
        gateway.Setup(candidate => candidate.GetPaymentLinkStatusAsync(555, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PayOS unreachable"));

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        openRenewal.Status.Should().Be("issued");
    }
}
