using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.UnitTests.TestSupport;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Subscriptions.SubscriptionServiceTests;

[TestClass]
public sealed class PayOsWebhookTests
{
    [TestMethod]
    public async Task HandlePayOsWebhookAsync_WhenSignatureIsInvalid_DoesNothing()
    {
        var invoices = new Mock<IInvoiceRepository>();
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(false, 0, false));
        var service = SubscriptionTestData.CreateService(invoices: invoices, paymentGateway: paymentGateway);

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        invoices.Verify(
            candidate => candidate.GetByPayosOrderCodeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task HandlePayOsWebhookAsync_WhenNoInvoiceMatchesTheOrderCode_DoesNothing()
    {
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByPayosOrderCodeAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(true, 123, true));
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = SubscriptionTestData.CreateService(invoices: invoices, paymentGateway: paymentGateway, unitOfWork: unitOfWork);

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task HandlePayOsWebhookAsync_WhenTheCheckoutAlreadyResolved_IsIdempotentAndDoesNothing()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.Status = "paid";

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByPayosOrderCodeAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(true, 123, true));
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = SubscriptionTestData.CreateService(invoices: invoices, paymentGateway: paymentGateway, unitOfWork: unitOfWork);

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task HandlePayOsWebhookAsync_OnSuccess_ActivatesTheSubscriptionAndMarksTheInvoicePaid()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        subscription.Status = "trialing";
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByPayosOrderCodeAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var subscriptions = new Mock<ISubscriptionRepository>();
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(true, 123, true));
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = SubscriptionTestData.CreateService(
            subscriptions, invoices: invoices, paymentGateway: paymentGateway, unitOfWork: unitOfWork);

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        invoices.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Invoice>(inv => inv.Status == "paid"), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub => sub.Status == "active"), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task HandlePayOsWebhookAsync_OnFailure_ExpiresTheSubscriptionAndMarksTheInvoiceFailed()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        subscription.Status = "trialing";
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByPayosOrderCodeAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var subscriptions = new Mock<ISubscriptionRepository>();
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(true, 123, false));
        var service = SubscriptionTestData.CreateService(subscriptions, invoices: invoices, paymentGateway: paymentGateway);

        await service.HandlePayOsWebhookAsync("{}", CancellationToken.None);

        invoices.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Invoice>(inv => inv.Status == "void"), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub => sub.Status == "expired"), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
