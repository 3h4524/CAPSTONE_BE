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
public sealed class CancelCheckoutTests
{
    [TestMethod]
    public async Task CancelCheckoutAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.IsAuthenticated).Returns(false);
        var service = SubscriptionTestData.CreateService(currentUser: currentUser);

        var result = await service.CancelCheckoutAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task CancelCheckoutAsync_WhenInvoiceDoesNotBelongToTheCaller_ReturnsInvoiceNotFound()
    {
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(It.IsAny<Guid>(), SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);
        var service = SubscriptionTestData.CreateService(invoices: invoices);

        var result = await service.CancelCheckoutAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionInvoiceNotFound);
    }

    [TestMethod]
    public async Task CancelCheckoutAsync_WhenAlreadyPaid_IsIdempotentAndDoesNotCallTheGateway()
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

        var result = await service.CancelCheckoutAsync(invoice.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        paymentGateway.Verify(
            candidate => candidate.CancelPaymentLinkAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CancelCheckoutAsync_WhenPending_CancelsOnPayosAndMarksLocalRowsCancelled()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        subscription.Status = "trialing";
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var subscriptions = new Mock<ISubscriptionRepository>();
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        var service = SubscriptionTestData.CreateService(subscriptions, invoices: invoices, paymentGateway: paymentGateway);

        var result = await service.CancelCheckoutAsync(invoice.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        paymentGateway.Verify(
            candidate => candidate.CancelPaymentLinkAsync(123, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        invoices.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Invoice>(inv => inv.Status == "void"), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub => sub.Status == "cancelled"), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CancelCheckoutAsync_WhenPayosCancelCallThrows_StillMarksLocalRowsCancelled()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        subscription.Status = "trialing";
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.CancelPaymentLinkAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PayOS unreachable"));
        var service = SubscriptionTestData.CreateService(invoices: invoices, paymentGateway: paymentGateway);

        var result = await service.CancelCheckoutAsync(invoice.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be("void");
    }
}
