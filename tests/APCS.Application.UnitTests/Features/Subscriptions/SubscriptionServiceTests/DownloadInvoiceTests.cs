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
public sealed class DownloadInvoiceTests
{
    private static Invoice CreatePaidInvoiceWithUser()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var subscription = SubscriptionTestData.CreateActiveSubscription(starter);
        var invoice = SubscriptionTestData.CreatePendingInvoice(subscription, 123);
        invoice.Status = "paid";
        invoice.User = new User
        {
            Id = SubscriptionTestData.UserId,
            Email = "seller@example.com",
            FullName = "Seller Name",
            AccountStatus = "active"
        };
        return invoice;
    }

    [TestMethod]
    public async Task DownloadInvoiceAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.IsAuthenticated).Returns(false);
        var service = SubscriptionTestData.CreateService(currentUser: currentUser);

        var result = await service.DownloadInvoiceAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task DownloadInvoiceAsync_WhenTheInvoiceCannotBeFound_ReturnsMsg65Text()
    {
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(
                It.IsAny<Guid>(), SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);
        var service = SubscriptionTestData.CreateService(invoices: invoices);

        var result = await service.DownloadInvoiceAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionInvoiceNotFound);
        result.Error.Message.Should().Be("This invoice could not be found. Please refresh and try again.");
    }

    [TestMethod]
    public async Task DownloadInvoiceAsync_WhenTheInvoiceExists_RendersItWithTheExpectedDetailAndFileName()
    {
        var invoice = CreatePaidInvoiceWithUser();
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        InvoicePdfModel? capturedModel = null;
        var renderer = new Mock<IInvoicePdfRenderer>();
        renderer.Setup(candidate => candidate.Render(It.IsAny<InvoicePdfModel>()))
            .Callback<InvoicePdfModel>(model => capturedModel = model)
            .Returns([1, 2, 3, 4]);
        var service = SubscriptionTestData.CreateService(invoices: invoices, invoicePdfRenderer: renderer);

        var result = await service.DownloadInvoiceAsync(invoice.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FileName.Should().Be($"{invoice.InvoiceNumber}.pdf");
        result.Value.Content.Should().Equal(1, 2, 3, 4);

        capturedModel.Should().NotBeNull();
        capturedModel!.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
        capturedModel.BilledToName.Should().Be("Seller Name");
        capturedModel.BilledToEmail.Should().Be("seller@example.com");
        capturedModel.PlanName.Should().Be(invoice.Subscription.Plan.Name);
        capturedModel.TotalPaid.Should().Be(invoice.TotalAmount);
        capturedModel.Status.Should().Be("paid");
    }

    [TestMethod]
    public async Task DownloadInvoiceAsync_WhenRenderingFails_ReturnsMsg66Text()
    {
        var invoice = CreatePaidInvoiceWithUser();
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetByIdForUserAsync(invoice.Id, SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var renderer = new Mock<IInvoicePdfRenderer>();
        renderer.Setup(candidate => candidate.Render(It.IsAny<InvoicePdfModel>()))
            .Throws(new InvalidOperationException("rendering failed"));
        var service = SubscriptionTestData.CreateService(invoices: invoices, invoicePdfRenderer: renderer);

        var result = await service.DownloadInvoiceAsync(invoice.Id, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionInvoicePdfGenerationFailed);
        result.Error.Message.Should().Be("Unable to generate the invoice PDF. Please try again.");
    }
}
