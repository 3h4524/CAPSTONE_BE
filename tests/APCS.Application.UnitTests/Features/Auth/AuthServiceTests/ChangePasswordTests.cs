using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class ChangePasswordTests
{
    [TestMethod]
    public async Task ChangePasswordAsync_WhenUserIsNotAuthenticated_ReturnsUnauthenticated()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.IsAuthenticated).Returns(false);
        var service = AuthTestData.CreateService(currentUser: currentUser);

        var result = await service.ChangePasswordAsync(
            new ChangePasswordRequestDto("OldPass1", "NewPass1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task ChangePasswordAsync_WhenCurrentPasswordIsWrong_ReturnsPasswordIncorrect()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        currentUser.Setup(u => u.UserId).Returns(AuthTestData.ActiveUser.Id);

        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.VerifyPasswordAsync(
                AuthTestData.ActiveUser.Id, "WrongPass1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = AuthTestData.CreateService(account, currentUser: currentUser);

        var result = await service.ChangePasswordAsync(
            new ChangePasswordRequestDto("WrongPass1", "NewPass1"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PasswordIncorrect);
    }

    [TestMethod]
    public async Task ChangePasswordAsync_WhenUserNotFound_ReturnsUserNotFound()
    {
        var ct = new CancellationTokenSource().Token;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        currentUser.Setup(u => u.UserId).Returns(AuthTestData.ActiveUser.Id);

        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.VerifyPasswordAsync(AuthTestData.ActiveUser.Id, "OldPass1", ct))
            .ReturnsAsync(true);
        account.Setup(svc => svc.UpdatePasswordAsync(AuthTestData.ActiveUser.Id, "NewPass1", ct))
            .ReturnsAsync(false);  // User disappeared between verify and update

        var service = AuthTestData.CreateService(account, currentUser: currentUser);

        var result = await service.ChangePasswordAsync(
            new ChangePasswordRequestDto("OldPass1", "NewPass1"),
            ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.UserNotFound);
    }

    [TestMethod]
    public async Task ChangePasswordAsync_WhenPasswordsAreValid_UpdatesPasswordAndSavesOnce()
    {
        var ct = new CancellationTokenSource().Token;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        currentUser.Setup(u => u.UserId).Returns(AuthTestData.ActiveUser.Id);

        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.VerifyPasswordAsync(AuthTestData.ActiveUser.Id, "OldPass1", ct))
            .ReturnsAsync(true);
        account.Setup(svc => svc.UpdatePasswordAsync(AuthTestData.ActiveUser.Id, "NewPass1", ct))
            .ReturnsAsync(true);

        var unitOfWork = new Mock<IUnitOfWork>();
        var service = AuthTestData.CreateService(account, unitOfWork, currentUser: currentUser);

        var result = await service.ChangePasswordAsync(
            new ChangePasswordRequestDto("OldPass1", "NewPass1"),
            ct);

        result.IsSuccess.Should().BeTrue();
        account.Verify(svc => svc.VerifyPasswordAsync(AuthTestData.ActiveUser.Id, "OldPass1", ct), Times.Once);
        account.Verify(svc => svc.UpdatePasswordAsync(AuthTestData.ActiveUser.Id, "NewPass1", ct), Times.Once);
        unitOfWork.Verify(uow => uow.SaveChangesAsync(ct), Times.Once);
    }

    [TestMethod]
    public async Task ChangePasswordAsync_DoesNotIssueOrRevokeAnyAuthTokens()
    {
        var ct = new CancellationTokenSource().Token;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        currentUser.Setup(u => u.UserId).Returns(AuthTestData.ActiveUser.Id);

        var account = new Mock<IAccountService>();
        account.Setup(svc => svc.VerifyPasswordAsync(AuthTestData.ActiveUser.Id, "OldPass1", ct))
            .ReturnsAsync(true);
        account.Setup(svc => svc.UpdatePasswordAsync(AuthTestData.ActiveUser.Id, "NewPass1", ct))
            .ReturnsAsync(true);

        var repository = new Mock<IAuthTokenRepository>();
        var service = AuthTestData.CreateService(account, authTokenRepository: repository, currentUser: currentUser);

        await service.ChangePasswordAsync(new ChangePasswordRequestDto("OldPass1", "NewPass1"), ct);

        repository.Verify(
            r => r.AddAsync(It.IsAny<APCS.Domain.Entities.AuthToken>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(
            r => r.GetRedeemableAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
