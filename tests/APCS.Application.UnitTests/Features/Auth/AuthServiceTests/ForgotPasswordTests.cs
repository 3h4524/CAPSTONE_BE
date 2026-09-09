using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class ForgotPasswordTests
{
    [TestMethod]
    public async Task ForgotPasswordAsync_WhenEmailNotFound_ReturnsSuccessWithoutIssuingToken()
    {
        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.FindByEmailAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((APCS.Application.Abstractions.Authentication.Dtos.AccountInfoDto?)null);
        var repository = new Mock<IAuthTokenRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = AuthTestData.CreateService(account, unitOfWork, repository);

        var result = await service.ForgotPasswordAsync(
            new ForgotPasswordRequestDto("user@example.com"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(
            r => r.AddAsync(It.IsAny<AuthToken>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ForgotPasswordAsync_WhenAccountEmailNotVerified_ReturnsSuccessWithoutIssuingToken()
    {
        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.FindByEmailAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.UnverifiedUser);
        var repository = new Mock<IAuthTokenRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = AuthTestData.CreateService(account, unitOfWork, repository);

        var result = await service.ForgotPasswordAsync(
            new ForgotPasswordRequestDto("user@example.com"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(
            r => r.AddAsync(It.IsAny<AuthToken>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ForgotPasswordAsync_WhenAccountIsInactive_ReturnsSuccessWithoutIssuingToken()
    {
        var inactiveUser = AuthTestData.ActiveUser with { IsActive = false };
        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.FindByEmailAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveUser);
        var repository = new Mock<IAuthTokenRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = AuthTestData.CreateService(account, unitOfWork, repository);

        var result = await service.ForgotPasswordAsync(
            new ForgotPasswordRequestDto("user@example.com"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(
            r => r.AddAsync(It.IsAny<AuthToken>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ForgotPasswordAsync_WhenAccountIsValid_RevokesOutstandingTokenAndIssuesNew()
    {
        var ct = new CancellationTokenSource().Token;
        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.FindByEmailAsync(AuthTestData.ActiveUser.Email, ct))
            .ReturnsAsync(AuthTestData.ActiveUser);

        var existing = AuthTestData.CreatePasswordResetToken();
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(r => r.GetRedeemableAsync(
                AuthTestData.ActiveUser.Id,
                AuthTokenTypes.PasswordReset,
                ct))
            .ReturnsAsync(new[] { existing });

        var unitOfWork = new Mock<IUnitOfWork>();
        var email = new Mock<IEmailService>();
        var jwtService = AuthTestData.CreateJwtService();
        var service = AuthTestData.CreateService(account, unitOfWork, repository, jwtService, emailService: email);

        var result = await service.ForgotPasswordAsync(
            new ForgotPasswordRequestDto(AuthTestData.ActiveUser.Email),
            ct);

        result.IsSuccess.Should().BeTrue();
        existing.IsRevoked.Should().BeTrue();
        repository.Verify(
            r => r.AddAsync(It.IsAny<AuthToken>(), It.IsAny<bool>(), ct),
            Times.Once);
        unitOfWork.Verify(uow => uow.SaveChangesAsync(ct), Times.Once);
        email.Verify(
            e => e.SendPasswordResetAsync(
                AuthTestData.ActiveUser.Email,
                AuthTestData.ActiveUser.FullName,
                AuthTestData.RawResetToken,
                ct),
            Times.Once);
    }

    [TestMethod]
    public async Task ForgotPasswordAsync_WhenEmailDeliveryFails_StillReturnsSuccess()
    {
        var ct = new CancellationTokenSource().Token;
        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.FindByEmailAsync(AuthTestData.ActiveUser.Email, ct))
            .ReturnsAsync(AuthTestData.ActiveUser);
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(r => r.GetRedeemableAsync(
                AuthTestData.ActiveUser.Id, AuthTokenTypes.PasswordReset, ct))
            .ReturnsAsync(Array.Empty<APCS.Domain.Entities.AuthToken>());
        var unitOfWork = new Mock<IUnitOfWork>();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendPasswordResetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), ct))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));
        var service = AuthTestData.CreateService(account, unitOfWork, repository, emailService: email);

        var result = await service.ForgotPasswordAsync(
            new ForgotPasswordRequestDto(AuthTestData.ActiveUser.Email),
            ct);

        result.IsSuccess.Should().BeTrue();
    }
}
