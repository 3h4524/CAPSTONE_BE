using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;

namespace APCS.Application.UnitTests.Features.Subscriptions.SubscriptionServiceTests;

[TestClass]
public sealed class CheckoutTests
{
    private static CheckoutRequestDto CreateRequest(Guid planId, string billingCycle = "monthly") =>
        new(planId, billingCycle);

    private static (Mock<IUnitOfWork> UnitOfWork, Mock<IUnitOfWorkTransaction> Transaction) CreateCommittingUnitOfWork(
        CancellationToken cancellationToken)
    {
        var transaction = SubscriptionTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.CommitAsync(cancellationToken)).Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.BeginTransactionAsync(cancellationToken))
            .ReturnsAsync(transaction.Object);
        return (unitOfWork, transaction);
    }

    private static (Mock<ISubscriptionRepository> Subscriptions, Mock<IPlanRepository> Plans)
        CreateHappyPathRepositories(SubscriptionPlan plan, Subscription? existingSubscription)
    {
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSubscription);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetPurchasableByIdAsync(plan.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        return (subscriptions, plans);
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenValidationFails_ReturnsFailureWithoutTouchingRepositories()
    {
        var validator = new Mock<IValidator<CheckoutRequestDto>>();
        validator.Setup(candidate => candidate.ValidateAsync(It.IsAny<CheckoutRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("PlanId", "Required")]));
        var subscriptions = new Mock<ISubscriptionRepository>();
        var service = SubscriptionTestData.CreateService(subscriptions, checkoutValidator: validator);

        var result = await service.InitiateCheckoutAsync(CreateRequest(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        subscriptions.Verify(
            candidate => candidate.GetActiveWithPlanAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.IsAuthenticated).Returns(false);
        var service = SubscriptionTestData.CreateService(currentUser: currentUser);

        var result = await service.InitiateCheckoutAsync(CreateRequest(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenSellerAlreadyHasAnActivePaidPlan_ReturnsAlreadySubscribedWithMsg57Text()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var activeSubscription = SubscriptionTestData.CreateActiveSubscription(starter);
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSubscription);
        var plans = new Mock<IPlanRepository>();
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.InitiateCheckoutAsync(
            CreateRequest(SubscriptionTestData.CreateProPlan().Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionAlreadySubscribed);
        result.Error.Message.Should().Be("You already have an active paid plan. Please use Upgrade or Downgrade instead.");
        plans.Verify(
            candidate => candidate.GetPurchasableByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenSellerIsOnTheFreePlan_StillCreatesAPendingCheckout()
    {
        var free = SubscriptionTestData.CreateFreePlan();
        var starter = SubscriptionTestData.CreateStarterPlan();
        var freeSubscription = SubscriptionTestData.CreateActiveSubscription(free);

        var (subscriptions, plans) = CreateHappyPathRepositories(starter, freeSubscription);
        var (unitOfWork, transaction) = CreateCommittingUnitOfWork(CancellationToken.None);
        var service = SubscriptionTestData.CreateService(subscriptions, plans, unitOfWork: unitOfWork);

        var result = await service.InitiateCheckoutAsync(CreateRequest(starter.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub => sub.Id == freeSubscription.Id && sub.Status == "cancelled"),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        subscriptions.Verify(
            candidate => candidate.AddAsync(
                It.Is<Subscription>(sub => sub.PlanId == starter.Id && sub.Status == "trialing"),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        transaction.Verify(candidate => candidate.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenPlanIsNotFound_ReturnsPlanNotFound()
    {
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetPurchasableByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionPlan?)null);
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subscription?)null);
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.InitiateCheckoutAsync(CreateRequest(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionPlanNotFound);
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenAnnualIsRequestedButPlanHasNoAnnualPrice_ReturnsAnnualNotAvailable()
    {
        var pro = SubscriptionTestData.CreateProPlan();
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subscription?)null);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetPurchasableByIdAsync(pro.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pro);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        var service = SubscriptionTestData.CreateService(subscriptions, plans, paymentGateway: paymentGateway);

        var result = await service.InitiateCheckoutAsync(CreateRequest(pro.Id, billingCycle: "annual"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionAnnualNotAvailable);
        paymentGateway.Verify(
            candidate => candidate.CreatePaymentLinkAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenATestAmountIsConfigured_ChargesTheTestAmountNotTheRealPrice()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var (subscriptions, plans) = CreateHappyPathRepositories(starter, null);
        var (unitOfWork, _) = CreateCommittingUnitOfWork(CancellationToken.None);
        var settings = SubscriptionTestData.CreateGatewaySettings(testAmountVnd: 2000);
        var service = SubscriptionTestData.CreateService(subscriptions, plans, unitOfWork: unitOfWork, gatewaySettings: settings);

        var result = await service.InitiateCheckoutAsync(CreateRequest(starter.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AmountVnd.Should().Be(2000);
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenNoTestAmountIsConfigured_ConvertsUsdToVndAtTheConfiguredRate()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var (subscriptions, plans) = CreateHappyPathRepositories(starter, null);
        var (unitOfWork, _) = CreateCommittingUnitOfWork(CancellationToken.None);
        var settings = SubscriptionTestData.CreateGatewaySettings(usdToVndRate: 25000m, testAmountVnd: null);
        var service = SubscriptionTestData.CreateService(subscriptions, plans, unitOfWork: unitOfWork, gatewaySettings: settings);

        var result = await service.InitiateCheckoutAsync(CreateRequest(starter.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AmountVnd.Should().Be((int)Math.Round(starter.MonthlyPriceUsd * 25000m));
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_StoresThePayosReferencesOnTheInvoice()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var (subscriptions, plans) = CreateHappyPathRepositories(starter, null);
        Invoice? savedInvoice = null;
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.AddAsync(It.IsAny<Invoice>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, bool, CancellationToken>((invoice, _, _) => savedInvoice = invoice)
            .Returns(Task.CompletedTask);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.CreatePaymentLinkAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLinkResult("link-123", "https://payos.vn/checkout/link-123", "raw-qr-payload"));
        var (unitOfWork, _) = CreateCommittingUnitOfWork(CancellationToken.None);
        var service = SubscriptionTestData.CreateService(
            subscriptions, plans, invoices, unitOfWork: unitOfWork, paymentGateway: paymentGateway);

        var result = await service.InitiateCheckoutAsync(CreateRequest(starter.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.QrCode.Should().Be("raw-qr-payload");
        result.Value.CheckoutUrl.Should().Be("https://payos.vn/checkout/link-123");
        savedInvoice.Should().NotBeNull();
        savedInvoice!.Status.Should().Be("issued");
        savedInvoice.PayosPaymentLinkId.Should().Be("link-123");
        savedInvoice.PayosQrCode.Should().Be("raw-qr-payload");
        savedInvoice.PayosOrderCode.Should().NotBeNull();
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenTheGatewayThrows_ReturnsGatewayUnavailableAndWritesNothing()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var (subscriptions, plans) = CreateHappyPathRepositories(starter, null);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.CreatePaymentLinkAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PayOS unreachable"));
        var service = SubscriptionTestData.CreateService(subscriptions, plans, paymentGateway: paymentGateway);

        var result = await service.InitiateCheckoutAsync(CreateRequest(starter.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PaymentGatewayUnavailable);
        subscriptions.Verify(
            candidate => candidate.AddAsync(It.IsAny<Subscription>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_WhenSaveChangesThrows_RollsBackAndRethrows()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var (subscriptions, plans) = CreateHappyPathRepositories(starter, null);

        var transaction = SubscriptionTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction.Object);
        unitOfWork.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var service = SubscriptionTestData.CreateService(subscriptions, plans, unitOfWork: unitOfWork);

        var act = () => service.InitiateCheckoutAsync(CreateRequest(starter.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        transaction.Verify(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(candidate => candidate.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
