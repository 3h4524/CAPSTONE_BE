using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;
using AuthTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class ResendVerificationEmailTests
{
    [TestMethod]
    public async Task ResendVerificationEmailAsync_WhenTheAccountIsPendingVerification_IssuesANewTokenAndSendsTheEmail()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountReturning(AuthTestData.UnverifiedUser, cancellationToken);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(candidate => candidate.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);
        AuthTokenEntity? savedToken = null;
        var repository = CreateRepository();
        repository.Setup(candidate => candidate.AddAsync(
                It.IsAny<AuthTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<AuthTokenEntity, bool, CancellationToken>((token, _, _) => savedToken = token)
            .Returns(Task.CompletedTask);
        var emailService = new Mock<IEmailService>();
        var service = AuthTestData.CreateService(account, unitOfWork, repository, emailService: emailService);

        var result = await service.ResendVerificationEmailAsync(
            new ResendVerificationEmailRequestDto(" User@Example.com "),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        savedToken.Should().NotBeNull();
        savedToken!.TokenType.Should().Be(AuthTokenTypes.EmailVerification);
        savedToken.TokenHash.Should().Be(AuthTestData.VerificationTokenHash);
        savedToken.ExpiresAt.Should().Be(AuthTestData.UtcNow.AddHours(24).UtcDateTime);
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(cancellationToken), Times.Once);
        emailService.Verify(
            candidate => candidate.SendEmailVerificationAsync(
                AuthTestData.UnverifiedUser.Email,
                AuthTestData.UnverifiedUser.FullName,
                AuthTestData.RawVerificationToken,
                cancellationToken),
            Times.Once);
    }

    [TestMethod]
    public async Task ResendVerificationEmailAsync_WhenTheEmailIsUnknown_ReportsSuccessWithoutSendingAnything()
    {
        var account = new Mock<IAccountService>();
        account.Setup(candidate => candidate.FindByEmailAsync(
                "user@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfoDto?)null);
        var repository = new Mock<IAuthTokenRepository>();
        var emailService = new Mock<IEmailService>();
        var service = AuthTestData.CreateService(
            account, new Mock<IUnitOfWork>(), repository, emailService: emailService);

        var result = await service.ResendVerificationEmailAsync(
            new ResendVerificationEmailRequestDto("user@example.com"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(
            candidate => candidate.AddAsync(
                It.IsAny<AuthTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        emailService.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ResendVerificationEmailAsync_WhenTheAccountIsAlreadyVerified_ReportsSuccessWithoutSendingAnything()
    {
        var account = CreateAccountReturning(AuthTestData.ActiveUser, CancellationToken.None);
        var repository = new Mock<IAuthTokenRepository>();
        var emailService = new Mock<IEmailService>();
        var service = AuthTestData.CreateService(
            account, new Mock<IUnitOfWork>(), repository, emailService: emailService);

        var result = await service.ResendVerificationEmailAsync(
            new ResendVerificationEmailRequestDto("user@example.com"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(
            candidate => candidate.AddAsync(
                It.IsAny<AuthTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        emailService.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ResendVerificationEmailAsync_WithRequestContext_RecordsTheCallerNetworkDetailsOnTheToken()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountReturning(AuthTestData.UnverifiedUser, cancellationToken);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(candidate => candidate.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);
        AuthTokenEntity? savedToken = null;
        var repository = CreateRepository();
        repository.Setup(candidate => candidate.AddAsync(
                It.IsAny<AuthTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<AuthTokenEntity, bool, CancellationToken>((token, _, _) => savedToken = token)
            .Returns(Task.CompletedTask);
        var service = AuthTestData.CreateService(account, unitOfWork, repository);

        await service.ResendVerificationEmailAsync(
            new ResendVerificationEmailRequestDto(
                "user@example.com",
                new RequestContext("203.0.113.5", "agent")),
            cancellationToken);

        savedToken.Should().NotBeNull();
        savedToken!.CreatedByIp.Should().Be("203.0.113.5");
        savedToken.UserAgent.Should().Be("agent");
    }

    [TestMethod]
    public async Task ResendVerificationEmailAsync_WhenALinkIsStillOutstanding_RevokesItBeforeIssuingTheReplacement()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountReturning(AuthTestData.UnverifiedUser, cancellationToken);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(candidate => candidate.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);

        // Already expired, which the database rule ignores: the row still occupies the single
        // redeemable slot for this user and token type until it is revoked.
        var outstanding = AuthTestData.CreateEmailVerificationToken(
            AuthTestData.UtcNow.AddHours(-1),
            "previous-token-hash");
        var repository = CreateRepository(outstanding);
        var service = AuthTestData.CreateService(account, unitOfWork, repository);

        var result = await service.ResendVerificationEmailAsync(
            new ResendVerificationEmailRequestDto("user@example.com"),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        outstanding.IsRevoked.Should().BeTrue();
        outstanding.RevokedAt.Should().Be(AuthTestData.UtcNow.UtcDateTime);
        repository.Verify(
            candidate => candidate.GetRedeemableAsync(
                AuthTestData.UnverifiedUser.Id,
                AuthTokenTypes.EmailVerification,
                cancellationToken),
            Times.Once);
    }

    private static Mock<IAccountService> CreateAccountReturning(
        AccountInfoDto account,
        CancellationToken cancellationToken)
    {
        var service = new Mock<IAccountService>();
        service.Setup(candidate => candidate.FindByEmailAsync("user@example.com", cancellationToken))
            .ReturnsAsync(account);
        return service;
    }

    private static Mock<IAuthTokenRepository> CreateRepository(params AuthTokenEntity[] outstanding)
    {
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(candidate => candidate.GetRedeemableAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(outstanding);
        return repository;
    }
}
