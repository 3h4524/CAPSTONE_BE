using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.UnitTests.TestSupport;
using APCS.Application.Features.Auth.Commands.Register;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.Commands.Register;

[TestClass]
public sealed class RegisterCommandHandlerTests
{
    [TestMethod]
    public async Task Handle_WhenEmailExists_ReturnsConflictWithoutStartingTransaction()
    {
        var account = new Mock<IAccountService>();
        account.Setup(service => service.EmailExistsAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var dbContext = new Mock<IUnitOfWork>();
        var repository = new Mock<IAuthTokenRepository>();
        var handler = CreateHandler(account, dbContext, repository, new Mock<IJwtService>());

        var result = await handler.Handle(
            new RegisterCommand("  User@Example.com ", "Password1", "User"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.EmailAlreadyExists);
        dbContext.Verify(
            context => context.BeginTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(candidate => candidate.Add(It.IsAny<RefreshTokenEntity>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenAccountCreationFails_RollsBackAndReturnsValidationFailure()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = new Mock<IAccountService>();
        account.Setup(service => service.EmailExistsAsync("user@example.com", cancellationToken))
            .ReturnsAsync(false);
        account.Setup(service => service.CreateUserAsync(
                "user@example.com",
                "Password1",
                "User Name",
                cancellationToken))
            .ReturnsAsync(AccountCreationResult.Failure("Duplicate user"));
        var transaction = AuthTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.RollbackAsync(cancellationToken)).Returns(Task.CompletedTask);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.BeginTransactionAsync(cancellationToken))
            .ReturnsAsync(transaction.Object);
        var handler = CreateHandler(
            account,
            dbContext,
            new Mock<IAuthTokenRepository>(),
            new Mock<IJwtService>());

        var result = await handler.Handle(
            new RegisterCommand(" User@Example.com ", "Password1", " User Name "),
            cancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error.Details!["identity"].Should().Equal("Duplicate user");
        transaction.Verify(candidate => candidate.RollbackAsync(cancellationToken), Times.Once);
        transaction.Verify(candidate => candidate.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenFullNameIsOmitted_DerivesItFromTheEmailLocalPart()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountForSuccessfulCreation("user", cancellationToken);
        var (dbContext, transaction) = CreateCommittingUnitOfWork(cancellationToken);
        var handler = CreateHandler(
            account,
            dbContext,
            new Mock<IAuthTokenRepository>(),
            AuthTestData.CreateJwtService());

        var result = await handler.Handle(
            new RegisterCommand("User@Example.com", "Password1"),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        account.Verify(
            service => service.CreateUserAsync("user@example.com", "Password1", "user", cancellationToken),
            Times.Once);
        transaction.Verify(candidate => candidate.CommitAsync(cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task Handle_WithValidRequest_PersistsSessionCommitsAndReturnsUser()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountForSuccessfulCreation("User Name", cancellationToken);
        var (dbContext, transaction) = CreateCommittingUnitOfWork(cancellationToken);
        RefreshTokenEntity? savedToken = null;
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(candidate => candidate.Add(It.IsAny<RefreshTokenEntity>()))
            .Callback<RefreshTokenEntity>(token => savedToken = token);
        var handler = CreateHandler(account, dbContext, repository, AuthTestData.CreateJwtService());

        var result = await handler.Handle(
            new RegisterCommand(" User@Example.com ", "Password1", " User Name "),
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
    public async Task Handle_WithValidRequest_DoesNotReloadTheUserItJustCreated()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountForSuccessfulCreation("User Name", cancellationToken);
        var (dbContext, _) = CreateCommittingUnitOfWork(cancellationToken);
        var handler = CreateHandler(
            account,
            dbContext,
            new Mock<IAuthTokenRepository>(),
            AuthTestData.CreateJwtService());

        await handler.Handle(
            new RegisterCommand("user@example.com", "Password1", "User Name"),
            cancellationToken);

        account.Verify(
            service => service.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        account.Verify(
            service => service.GetRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenUnexpectedExceptionOccurs_RollsBackAndRethrows()
    {
        var expected = new InvalidOperationException("account store unavailable");
        var account = new Mock<IAccountService>();
        account.Setup(service => service.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        account.Setup(service => service.CreateUserAsync(
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
            account,
            dbContext,
            new Mock<IAuthTokenRepository>(),
            new Mock<IJwtService>());

        var act = () => handler.Handle(
            new RegisterCommand("user@example.com", "Password1", "User"),
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expected);
        transaction.Verify(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IAccountService> CreateAccountForSuccessfulCreation(
        string expectedFullName,
        CancellationToken cancellationToken)
    {
        var account = new Mock<IAccountService>();
        account.Setup(service => service.EmailExistsAsync("user@example.com", cancellationToken))
            .ReturnsAsync(false);
        account.Setup(service => service.CreateUserAsync(
                "user@example.com",
                "Password1",
                expectedFullName,
                cancellationToken))
            .ReturnsAsync(AccountCreationResult.Success(AuthTestData.ActiveUser, AuthTestData.Roles));
        return account;
    }

    private static (Mock<IUnitOfWork> UnitOfWork, Mock<IUnitOfWorkTransaction> Transaction) CreateCommittingUnitOfWork(
        CancellationToken cancellationToken)
    {
        var transaction = AuthTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.CommitAsync(cancellationToken)).Returns(Task.CompletedTask);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.BeginTransactionAsync(cancellationToken))
            .ReturnsAsync(transaction.Object);
        dbContext.Setup(context => context.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);
        return (dbContext, transaction);
    }

    private static RegisterCommandHandler CreateHandler(
        Mock<IAccountService> account,
        Mock<IUnitOfWork> dbContext,
        Mock<IAuthTokenRepository> repository,
        Mock<IJwtService> jwtService) => new(
        account.Object,
        dbContext.Object,
        repository.Object,
        jwtService.Object,
        AuthTestData.CreateTimeProvider());
}
