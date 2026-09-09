using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class RegisterTests
{
    [TestMethod]
    public async Task RegisterAsync_WhenEmailExists_ReturnsConflictWithoutStartingTransaction()
    {
        var account = new Mock<IAccountService>();
        account.Setup(service => service.EmailExistsAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var unitOfWork = new Mock<IUnitOfWork>();
        var repository = new Mock<IAuthTokenRepository>();
        var service = AuthTestData.CreateService(account, unitOfWork, repository);

        var result = await service.RegisterAsync(
            new RegisterRequestDto("  User@Example.com ", "Password1", "User"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.EmailAlreadyExists);
        unitOfWork.Verify(
            context => context.BeginTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(
            candidate => candidate.AddAsync(
                It.IsAny<RefreshTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task RegisterAsync_WhenAccountCreationFails_RollsBackAndReturnsValidationFailure()
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
            .ReturnsAsync(AccountCreationResultDto.Failure("Duplicate user"));
        var transaction = AuthTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.RollbackAsync(cancellationToken)).Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.BeginTransactionAsync(cancellationToken))
            .ReturnsAsync(transaction.Object);
        var service = AuthTestData.CreateService(account, unitOfWork);

        var result = await service.RegisterAsync(
            new RegisterRequestDto(" User@Example.com ", "Password1", " User Name "),
            cancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error.Details!["identity"].Should().Equal("Duplicate user");
        transaction.Verify(candidate => candidate.RollbackAsync(cancellationToken), Times.Once);
        transaction.Verify(candidate => candidate.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RegisterAsync_WhenFullNameIsOmitted_DerivesItFromTheEmailLocalPart()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountForSuccessfulCreation("user", cancellationToken);
        var (unitOfWork, transaction) = CreateCommittingUnitOfWork(cancellationToken);
        var service = AuthTestData.CreateService(account, unitOfWork, jwtService: AuthTestData.CreateJwtService());

        var result = await service.RegisterAsync(
            new RegisterRequestDto("User@Example.com", "Password1"),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        account.Verify(
            service => service.CreateUserAsync("user@example.com", "Password1", "user", cancellationToken),
            Times.Once);
        transaction.Verify(candidate => candidate.CommitAsync(cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task RegisterAsync_WithValidRequest_PersistsVerificationTokenCommitsAndReturnsAccount()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountForSuccessfulCreation("User Name", cancellationToken);
        var (unitOfWork, transaction) = CreateCommittingUnitOfWork(cancellationToken);
        RefreshTokenEntity? savedToken = null;
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(candidate => candidate.AddAsync(
                It.IsAny<RefreshTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshTokenEntity, bool, CancellationToken>((token, _, _) => savedToken = token)
            .Returns(Task.CompletedTask);
        var service = AuthTestData.CreateService(
            account, unitOfWork, repository, AuthTestData.CreateJwtService());

        var result = await service.RegisterAsync(
            new RegisterRequestDto(" User@Example.com ", "Password1", " User Name "),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(AuthTestData.ActiveUser.Id);
        result.Value.Email.Should().Be(AuthTestData.ActiveUser.Email);
        result.Value.RequiresEmailVerification.Should().BeTrue();
        savedToken.Should().NotBeNull();
        savedToken!.TokenType.Should().Be(AuthTokenTypes.EmailVerification);
        savedToken.TokenHash.Should().Be(AuthTestData.VerificationTokenHash);
        unitOfWork.Verify(context => context.SaveChangesAsync(cancellationToken), Times.Once);
        transaction.Verify(candidate => candidate.CommitAsync(cancellationToken), Times.Once);
        transaction.Verify(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RegisterAsync_WithValidRequest_SendsTheVerificationEmailWithTheRawToken()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountForSuccessfulCreation("User Name", cancellationToken);
        var (unitOfWork, _) = CreateCommittingUnitOfWork(cancellationToken);
        var emailService = new Mock<IEmailService>();
        var service = AuthTestData.CreateService(
            account, unitOfWork, jwtService: AuthTestData.CreateJwtService(), emailService: emailService);

        await service.RegisterAsync(
            new RegisterRequestDto("user@example.com", "Password1", "User Name"),
            cancellationToken);

        emailService.Verify(
            candidate => candidate.SendEmailVerificationAsync(
                AuthTestData.ActiveUser.Email,
                AuthTestData.ActiveUser.FullName,
                AuthTestData.RawVerificationToken,
                cancellationToken),
            Times.Once);
    }

    [TestMethod]
    public async Task RegisterAsync_WhenTheVerificationEmailFails_StillReportsSuccessSoTheUserCanResend()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountForSuccessfulCreation("User Name", cancellationToken);
        var (unitOfWork, transaction) = CreateCommittingUnitOfWork(cancellationToken);
        var emailService = new Mock<IEmailService>();
        emailService.Setup(candidate => candidate.SendEmailVerificationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp unavailable"));
        var service = AuthTestData.CreateService(
            account, unitOfWork, jwtService: AuthTestData.CreateJwtService(), emailService: emailService);

        var result = await service.RegisterAsync(
            new RegisterRequestDto("user@example.com", "Password1", "User Name"),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        transaction.Verify(candidate => candidate.CommitAsync(cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task RegisterAsync_WithValidRequest_DoesNotIssueASession()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountForSuccessfulCreation("User Name", cancellationToken);
        var (unitOfWork, _) = CreateCommittingUnitOfWork(cancellationToken);
        var jwtService = AuthTestData.CreateJwtService();
        var service = AuthTestData.CreateService(account, unitOfWork, jwtService: jwtService);

        await service.RegisterAsync(
            new RegisterRequestDto("user@example.com", "Password1", "User Name"),
            cancellationToken);

        jwtService.Verify(
            candidate => candidate.GenerateAccessToken(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>()),
            Times.Never);
        jwtService.Verify(candidate => candidate.GenerateRefreshToken(), Times.Never);
    }

    [TestMethod]
    public async Task RegisterAsync_WithValidRequest_DoesNotReloadTheUserItJustCreated()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountForSuccessfulCreation("User Name", cancellationToken);
        var (unitOfWork, _) = CreateCommittingUnitOfWork(cancellationToken);
        var service = AuthTestData.CreateService(
            account, unitOfWork, jwtService: AuthTestData.CreateJwtService());

        await service.RegisterAsync(
            new RegisterRequestDto("user@example.com", "Password1", "User Name"),
            cancellationToken);

        account.Verify(
            service => service.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        account.Verify(
            service => service.GetRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task RegisterAsync_WhenUnexpectedExceptionOccurs_RollsBackAndRethrows()
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
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction.Object);
        var service = AuthTestData.CreateService(account, unitOfWork);

        var act = () => service.RegisterAsync(
            new RegisterRequestDto("user@example.com", "Password1", "User"),
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
            .ReturnsAsync(AccountCreationResultDto.Success(AuthTestData.ActiveUser, AuthTestData.Roles));
        return account;
    }

    private static (Mock<IUnitOfWork> UnitOfWork, Mock<IUnitOfWorkTransaction> Transaction) CreateCommittingUnitOfWork(
        CancellationToken cancellationToken)
    {
        var transaction = AuthTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.CommitAsync(cancellationToken)).Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.BeginTransactionAsync(cancellationToken))
            .ReturnsAsync(transaction.Object);
        unitOfWork.Setup(context => context.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);
        return (unitOfWork, transaction);
    }
}
