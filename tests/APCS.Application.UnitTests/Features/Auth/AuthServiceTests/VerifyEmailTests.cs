using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;
using AuthTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class VerifyEmailTests
{
    [TestMethod]
    public async Task VerifyEmailAsync_WhenTokenIsUnknown_ReturnsInvalidWithoutSaving()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = AuthTestData.CreateService(
            new Mock<IAccountService>(), unitOfWork, CreateRepository(null));

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequestDto(AuthTestData.RawVerificationToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.VerificationTokenInvalid);
        unitOfWork.Verify(
            candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task VerifyEmailAsync_WhenTokenIsARefreshToken_ReturnsInvalidWithoutConfirming()
    {
        var account = new Mock<IAccountService>();
        var repository = CreateRepository(
            AuthTestData.CreateRefreshToken(tokenHash: AuthTestData.VerificationTokenHash));
        var service = AuthTestData.CreateService(account, new Mock<IUnitOfWork>(), repository);

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequestDto(AuthTestData.RawVerificationToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.VerificationTokenInvalid);
        account.Verify(
            candidate => candidate.ConfirmEmailAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task VerifyEmailAsync_WhenTokenHasExpired_ReturnsExpired()
    {
        var expired = AuthTestData.CreateEmailVerificationToken(AuthTestData.UtcNow.AddHours(-1));
        var service = AuthTestData.CreateService(
            new Mock<IAccountService>(), new Mock<IUnitOfWork>(), CreateRepository(expired));

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequestDto(AuthTestData.RawVerificationToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.VerificationTokenExpired);
    }

    [TestMethod]
    public async Task VerifyEmailAsync_WhenTokenWasAlreadyRedeemed_ReturnsInvalid()
    {
        var token = AuthTestData.CreateEmailVerificationToken();
        token.MarkUsed(AuthTestData.UtcNow.AddMinutes(-5));
        var service = AuthTestData.CreateService(
            new Mock<IAccountService>(), new Mock<IUnitOfWork>(), CreateRepository(token));

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequestDto(AuthTestData.RawVerificationToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.VerificationTokenInvalid);
    }

    [TestMethod]
    public async Task VerifyEmailAsync_WhenTokenWasRevoked_ReturnsInvalid()
    {
        var token = AuthTestData.CreateEmailVerificationToken();
        token.Revoke(AuthTestData.UtcNow.AddMinutes(-5));
        var service = AuthTestData.CreateService(
            new Mock<IAccountService>(), new Mock<IUnitOfWork>(), CreateRepository(token));

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequestDto(AuthTestData.RawVerificationToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.VerificationTokenInvalid);
    }

    [TestMethod]
    public async Task VerifyEmailAsync_WhenTheAccountNoLongerExists_ReturnsInvalid()
    {
        var account = new Mock<IAccountService>();
        account.Setup(candidate => candidate.FindByIdAsync(
                AuthTestData.ActiveUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfoDto?)null);
        var service = AuthTestData.CreateService(
            account, new Mock<IUnitOfWork>(), CreateRepository(AuthTestData.CreateEmailVerificationToken()));

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequestDto(AuthTestData.RawVerificationToken),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.VerificationTokenInvalid);
    }

    [TestMethod]
    public async Task VerifyEmailAsync_WithAValidToken_ConfirmsTheEmailSpendsTheTokenAndSavesOnce()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var token = AuthTestData.CreateEmailVerificationToken();
        var account = CreateAccountFor(AuthTestData.UnverifiedUser, cancellationToken);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(candidate => candidate.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);
        var service = AuthTestData.CreateService(account, unitOfWork, CreateRepository(token));

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequestDto(AuthTestData.RawVerificationToken),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        token.IsUsed.Should().BeTrue();
        token.UsedAt.Should().Be(AuthTestData.UtcNow.UtcDateTime);
        account.Verify(
            candidate => candidate.ConfirmEmailAsync(
                AuthTestData.ActiveUser.Id,
                AuthTestData.UtcNow,
                cancellationToken),
            Times.Once);
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task VerifyEmailAsync_WhenTheAccountIsAlreadyVerified_SpendsTheTokenWithoutConfirmingAgain()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var token = AuthTestData.CreateEmailVerificationToken();
        var account = CreateAccountFor(AuthTestData.ActiveUser, cancellationToken);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(candidate => candidate.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);
        var service = AuthTestData.CreateService(account, unitOfWork, CreateRepository(token));

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequestDto(AuthTestData.RawVerificationToken),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        token.IsUsed.Should().BeTrue();
        account.Verify(
            candidate => candidate.ConfirmEmailAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task VerifyEmailAsync_WhenConfirmationFails_LeavesTheTokenUnspentAndDoesNotSave()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var token = AuthTestData.CreateEmailVerificationToken();
        var account = CreateAccountFor(AuthTestData.UnverifiedUser, cancellationToken);
        account.Setup(candidate => candidate.ConfirmEmailAsync(
                AuthTestData.ActiveUser.Id,
                AuthTestData.UtcNow,
                cancellationToken))
            .ReturnsAsync(false);
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = AuthTestData.CreateService(account, unitOfWork, CreateRepository(token));

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequestDto(AuthTestData.RawVerificationToken),
            cancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.VerificationTokenInvalid);
        token.IsUsed.Should().BeFalse();
        unitOfWork.Verify(
            candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<IAccountService> CreateAccountFor(
        AccountInfoDto account,
        CancellationToken cancellationToken)
    {
        var service = new Mock<IAccountService>();
        service.Setup(candidate => candidate.FindByIdAsync(account.Id, cancellationToken))
            .ReturnsAsync(account);
        service.Setup(candidate => candidate.ConfirmEmailAsync(
                account.Id,
                AuthTestData.UtcNow,
                cancellationToken))
            .ReturnsAsync(true);
        return service;
    }

    private static Mock<IAuthTokenRepository> CreateRepository(AuthTokenEntity? token)
    {
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(candidate => candidate.GetByHashAsync(
                AuthTestData.VerificationTokenHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        return repository;
    }
}
