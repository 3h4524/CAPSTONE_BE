using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.UnitTests.TestSupport;
using APCS.Application.Features.Auth.Commands.Register;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.RefreshToken;

namespace APCS.Application.UnitTests.Features.Auth.Commands.Register;

[TestClass]
public sealed class RegisterCommandHandlerTests
{
    [TestMethod]
    public async Task Handle_WhenEmailExists_ReturnsConflictWithoutStartingTransaction()
    {
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.EmailExistsAsync("seller@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var dbContext = new Mock<IUnitOfWork>();
        var repository = new Mock<IRefreshTokenRepository>();
        var handler = CreateHandler(identity, dbContext, repository, new Mock<IJwtService>());

        var result = await handler.Handle(
            new RegisterCommand("  Seller@Example.com ", "Password1", "Seller"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.EmailAlreadyExists);
        dbContext.Verify(
            context => context.BeginTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(candidate => candidate.Add(It.IsAny<RefreshTokenEntity>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenIdentityCreationFails_RollsBackAndReturnsValidationFailure()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.EmailExistsAsync("seller@example.com", cancellationToken))
            .ReturnsAsync(false);
        identity.Setup(service => service.CreateUserAsync(
                "seller@example.com",
                "Password1",
                "Seller Name",
                cancellationToken))
            .ReturnsAsync(IdentityOperationResult.Failure(["Duplicate user"]));
        var transaction = AuthTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.RollbackAsync(cancellationToken)).Returns(Task.CompletedTask);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.BeginTransactionAsync(cancellationToken))
            .ReturnsAsync(transaction.Object);
        var handler = CreateHandler(
            identity,
            dbContext,
            new Mock<IRefreshTokenRepository>(),
            new Mock<IJwtService>());

        var result = await handler.Handle(
            new RegisterCommand(" Seller@Example.com ", "Password1", " Seller Name "),
            cancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error.Details!["identity"].Should().Equal("Duplicate user");
        transaction.Verify(candidate => candidate.RollbackAsync(cancellationToken), Times.Once);
        transaction.Verify(candidate => candidate.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenCreatedUserCannotBeLoaded_RollsBackAndReturnsFailure()
    {
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.EmailExistsAsync("seller@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        identity.Setup(service => service.CreateUserAsync(
                "seller@example.com",
                "Password1",
                "Seller",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());
        identity.Setup(service => service.FindByEmailAsync("seller@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUserInfo?)null);
        var transaction = AuthTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction.Object);
        var handler = CreateHandler(
            identity,
            dbContext,
            new Mock<IRefreshTokenRepository>(),
            new Mock<IJwtService>());

        var result = await handler.Handle(
            new RegisterCommand("seller@example.com", "Password1", "Seller"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unexpected);
        transaction.Verify(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Handle_WithValidRequest_PersistsSessionCommitsAndReturnsUser()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.EmailExistsAsync("seller@example.com", cancellationToken))
            .ReturnsAsync(false);
        identity.Setup(service => service.CreateUserAsync(
                "seller@example.com",
                "Password1",
                "Seller Name",
                cancellationToken))
            .ReturnsAsync(IdentityOperationResult.Success());
        identity.Setup(service => service.FindByEmailAsync("seller@example.com", cancellationToken))
            .ReturnsAsync(AuthTestData.ActiveUser);
        identity.Setup(service => service.GetRolesAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.Roles);
        var transaction = AuthTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.CommitAsync(cancellationToken)).Returns(Task.CompletedTask);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.BeginTransactionAsync(cancellationToken))
            .ReturnsAsync(transaction.Object);
        dbContext.Setup(context => context.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);
        RefreshTokenEntity? savedToken = null;
        var repository = new Mock<IRefreshTokenRepository>();
        repository.Setup(candidate => candidate.Add(It.IsAny<RefreshTokenEntity>()))
            .Callback<RefreshTokenEntity>(token => savedToken = token);
        var jwtService = AuthTestData.CreateJwtService();
        var handler = CreateHandler(identity, dbContext, repository, jwtService);

        var result = await handler.Handle(
            new RegisterCommand(" Seller@Example.com ", "Password1", " Seller Name "),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access-token");
        result.Value.RefreshToken.Should().Be("new-raw-refresh-token");
        result.Value.User.Email.Should().Be(AuthTestData.ActiveUser.Email);
        savedToken.Should().NotBeNull();
        savedToken!.TokenHash.Should().Be("new-refresh-token-hash");
        dbContext.Verify(context => context.SaveChangesAsync(cancellationToken), Times.Once);
        transaction.Verify(candidate => candidate.CommitAsync(cancellationToken), Times.Once);
        transaction.Verify(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenUnexpectedExceptionOccurs_RollsBackAndRethrows()
    {
        var expected = new InvalidOperationException("identity unavailable");
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        identity.Setup(service => service.CreateUserAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var transaction = AuthTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction.Object);
        var handler = CreateHandler(
            identity,
            dbContext,
            new Mock<IRefreshTokenRepository>(),
            new Mock<IJwtService>());

        var act = () => handler.Handle(
            new RegisterCommand("seller@example.com", "Password1", "Seller"),
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expected);
        transaction.Verify(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static RegisterCommandHandler CreateHandler(
        Mock<IIdentityService> identity,
        Mock<IUnitOfWork> dbContext,
        Mock<IRefreshTokenRepository> repository,
        Mock<IJwtService> jwtService) => new(
        identity.Object,
        dbContext.Object,
        repository.Object,
        jwtService.Object,
        AuthTestData.CreateTimeProvider());
}
