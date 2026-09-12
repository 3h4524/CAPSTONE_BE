using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Subscriptions.SubscriptionServiceTests;

[TestClass]
public sealed class CheckoutStatusTests
{
    [TestMethod]
    public async Task GetCheckoutStatusAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.IsAuthenticated).Returns(false);
        var service = SubscriptionTestData.CreateService(currentUser: currentUser);

        var result = await service.GetCheckoutStatusAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task GetCheckoutStatusAsync_WhenInvoiceDoesNotBelongToTheCaller_ReturnsInvoiceNotFound()
    {
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(It.IsAny<Guid>(), SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);
        var service = SubscriptionTestData.CreateService(invoices: invoices);

        var result = await service.GetCheckoutStatusAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionInvoiceNotFound);
    }

    [TestMethod]
    public async Task GetCheckoutStatusAsync_WhenAlreadyPaid_ReturnsPaidWithoutCallingTheGateway()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.Status = "paid";

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        var service = SubscriptionTestData.CreateService(invoices: invoices, paymentGateway: paymentGateway);

        var result = await service.GetCheckoutStatusAsync(invoice.Id, CancellationToken.None);

        result.Value.Status.Should().Be("paid");
        paymentGateway.Verify(
            candidate => candidate.GetPaymentLinkStatusAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetCheckoutStatusAsync_WhenVoidWithACancelledSubscription_ReturnsCancelledNotFailed()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        subscription.Status = "cancelled";
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.Status = "void";

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var service = SubscriptionTestData.CreateService(invoices: invoices);

        var result = await service.GetCheckoutStatusAsync(invoice.Id, CancellationToken.None);

        result.Value.Status.Should().Be("cancelled");
    }

    [TestMethod]
    public async Task GetCheckoutStatusAsync_WhenVoidWithAnExpiredSubscription_ReturnsFailedNotCancelled()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        subscription.Status = "expired";
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.Status = "void";

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var service = SubscriptionTestData.CreateService(invoices: invoices);

        var result = await service.GetCheckoutStatusAsync(invoice.Id, CancellationToken.None);

        result.Value.Status.Should().Be("failed");
    }

    [TestMethod]
    public async Task GetCheckoutStatusAsync_WhenPendingAndRecentlyCreated_DoesNotReconcileWithTheGatewayYet()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.CreatedAt = SubscriptionTestData.CreateTimeProvider().GetUtcNow().UtcDateTime;

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        var service = SubscriptionTestData.CreateService(
            invoices: invoices, paymentGateway: paymentGateway, timeProvider: SubscriptionTestData.CreateTimeProvider());

        var result = await service.GetCheckoutStatusAsync(invoice.Id, CancellationToken.None);

        result.Value.Status.Should().Be("pending");
        paymentGateway.Verify(
            candidate => candidate.GetPaymentLinkStatusAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetCheckoutStatusAsync_WhenPendingForAWhileAndGatewayReportsPaid_ActivatesTheSubscription()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        subscription.Status = "trialing";
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.CreatedAt = SubscriptionTestData.UtcNow.AddSeconds(-30).UtcDateTime;

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var subscriptions = new Mock<ISubscriptionRepository>();
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.GetPaymentLinkStatusAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLinkStatusResult(true, false));
        var service = SubscriptionTestData.CreateService(
            subscriptions, invoices: invoices, paymentGateway: paymentGateway,
            timeProvider: SubscriptionTestData.CreateTimeProvider());

        var result = await service.GetCheckoutStatusAsync(invoice.Id, CancellationToken.None);

        result.Value.Status.Should().Be("paid");
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub => sub.Status == "active"), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetCheckoutStatusAsync_WhenPendingForAWhileAndGatewayReportsFailed_ExpiresTheSubscription()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        subscription.Status = "trialing";
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.CreatedAt = SubscriptionTestData.UtcNow.AddSeconds(-30).UtcDateTime;

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var subscriptions = new Mock<ISubscriptionRepository>();
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.GetPaymentLinkStatusAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLinkStatusResult(false, true));
        var service = SubscriptionTestData.CreateService(
            subscriptions, invoices: invoices, paymentGateway: paymentGateway,
            timeProvider: SubscriptionTestData.CreateTimeProvider());

        var result = await service.GetCheckoutStatusAsync(invoice.Id, CancellationToken.None);

        result.Value.Status.Should().Be("failed");
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub => sub.Status == "expired"), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetCheckoutStatusAsync_WhenGatewayThrowsDuringReconciliation_StillReturnsPendingInsteadOfFailing()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        subscription.Status = "trialing";
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.CreatedAt = SubscriptionTestData.UtcNow.AddSeconds(-30).UtcDateTime;

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.GetPaymentLinkStatusAsync(123, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PayOS unreachable"));
        var service = SubscriptionTestData.CreateService(
            invoices: invoices, paymentGateway: paymentGateway, timeProvider: SubscriptionTestData.CreateTimeProvider());

        var result = await service.GetCheckoutStatusAsync(invoice.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("pending");
    }
}
