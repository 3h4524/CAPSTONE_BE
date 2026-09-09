using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class ResetPasswordTests
{
    [TestMethod]
    public async Task ResetPasswordAsync_WhenTokenNotFound_ReturnsPasswordResetTokenInvalid()
    {
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthToken?)null);
        var service = AuthTestData.CreateService(authTokenRepository: repository);

        var result = await service.ResetPasswordAsync(
            new ResetPasswordRequestDto(AuthTestData.RawResetToken, "NewPass1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PasswordResetTokenInvalid);
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WhenTokenIsWrongType_ReturnsPasswordResetTokenInvalid()
    {
        // A verification token has the same hash structure but the wrong TokenType.
        var wrongToken = AuthTestData.CreateEmailVerificationToken(
            tokenHash: AuthTestData.ResetTokenHash);
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(r => r.GetByHashAsync(AuthTestData.ResetTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wrongToken);
        var jwtService = AuthTestData.CreateJwtService();
        jwtService.Setup(j => j.HashPasswordResetToken(AuthTestData.RawResetToken))
            .Returns(AuthTestData.ResetTokenHash);
        var service = AuthTestData.CreateService(authTokenRepository: repository, jwtService: jwtService);

        var result = await service.ResetPasswordAsync(
            new ResetPasswordRequestDto(AuthTestData.RawResetToken, "NewPass1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PasswordResetTokenInvalid);
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WhenTokenIsExpired_ReturnsPasswordResetTokenExpired()
    {
        var expiredToken = AuthTestData.CreatePasswordResetToken(
            expiresAtUtc: AuthTestData.UtcNow.AddHours(-1));
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(r => r.GetByHashAsync(AuthTestData.ResetTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken);
        var jwtService = AuthTestData.CreateJwtService();
        var service = AuthTestData.CreateService(authTokenRepository: repository, jwtService: jwtService);

        var result = await service.ResetPasswordAsync(
            new ResetPasswordRequestDto(AuthTestData.RawResetToken, "NewPass1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PasswordResetTokenExpired);
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WhenTokenIsRevoked_ReturnsPasswordResetTokenInvalid()
    {
        var revokedToken = AuthTestData.CreatePasswordResetToken();
        revokedToken.Revoke(AuthTestData.UtcNow.AddMinutes(-1));
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(r => r.GetByHashAsync(AuthTestData.ResetTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokedToken);
        var jwtService = AuthTestData.CreateJwtService();
        var service = AuthTestData.CreateService(authTokenRepository: repository, jwtService: jwtService);

        var result = await service.ResetPasswordAsync(
            new ResetPasswordRequestDto(AuthTestData.RawResetToken, "NewPass1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PasswordResetTokenInvalid);
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WhenTokenIsAlreadyUsed_ReturnsPasswordResetTokenInvalid()
    {
        var usedToken = AuthTestData.CreatePasswordResetToken();
        usedToken.MarkUsed(AuthTestData.UtcNow.AddMinutes(-5));
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(r => r.GetByHashAsync(AuthTestData.ResetTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usedToken);
        var jwtService = AuthTestData.CreateJwtService();
        var service = AuthTestData.CreateService(authTokenRepository: repository, jwtService: jwtService);

        var result = await service.ResetPasswordAsync(
            new ResetPasswordRequestDto(AuthTestData.RawResetToken, "NewPass1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PasswordResetTokenInvalid);
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WhenTokenIsValid_UpdatesPasswordAndSpendsToken()
    {
        var ct = new CancellationTokenSource().Token;
        var validToken = AuthTestData.CreatePasswordResetToken();
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(r => r.GetByHashAsync(AuthTestData.ResetTokenHash, ct))
            .ReturnsAsync(validToken);

        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.FindByIdAsync(AuthTestData.ActiveUser.Id, ct))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(svc => svc.UpdatePasswordAsync(AuthTestData.ActiveUser.Id, "NewPass1", ct))
            .ReturnsAsync(true);

        var unitOfWork = new Mock<IUnitOfWork>();
        var jwtService = AuthTestData.CreateJwtService();
        var service = AuthTestData.CreateService(account, unitOfWork, repository, jwtService);

        var result = await service.ResetPasswordAsync(
            new ResetPasswordRequestDto(AuthTestData.RawResetToken, "NewPass1"),
            ct);

        result.IsSuccess.Should().BeTrue();
        validToken.IsUsed.Should().BeTrue();
        account.Verify(svc => svc.UpdatePasswordAsync(AuthTestData.ActiveUser.Id, "NewPass1", ct), Times.Once);
        unitOfWork.Verify(uow => uow.SaveChangesAsync(ct), Times.Once);
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WhenAccountIsInactive_ReturnsPasswordResetTokenInvalid()
    {
        var validToken = AuthTestData.CreatePasswordResetToken();
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(r => r.GetByHashAsync(AuthTestData.ResetTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validToken);

        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.FindByIdAsync(AuthTestData.ActiveUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser with { IsActive = false });

        var jwtService = AuthTestData.CreateJwtService();
        var service = AuthTestData.CreateService(account, authTokenRepository: repository, jwtService: jwtService);

        var result = await service.ResetPasswordAsync(
            new ResetPasswordRequestDto(AuthTestData.RawResetToken, "NewPass1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PasswordResetTokenInvalid);
    }
}
