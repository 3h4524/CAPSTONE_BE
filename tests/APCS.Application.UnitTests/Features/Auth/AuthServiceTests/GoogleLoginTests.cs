using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Caching;
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
public sealed class GoogleLoginTests
{
    private const string ValidIdToken = "valid-google-id-token";

    private static readonly GoogleUserInfoDto ValidGoogleUser = new(
        "google-id-123",
        "user@example.com",
        true,
        "Google User",
        "https://avatar.url");

    [TestMethod]
    public async Task GoogleLoginAsync_WhenValidationFails_ReturnsValidationError()
    {
        var validator = new Mock<FluentValidation.IValidator<GoogleLoginRequestDto>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<GoogleLoginRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FluentValidation.Results.ValidationResult(new[] { new FluentValidation.Results.ValidationFailure("Prop", "Error") }));

        var result = await AuthTestData.CreateService(
                googleLoginValidator: validator.Object)
            .GoogleLoginAsync(new GoogleLoginRequestDto(""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [TestMethod]
    public async Task GoogleLoginAsync_WhenGoogleTokenInvalid_ReturnsInvalid()
    {
        var googleAuth = new Mock<IGoogleAuthService>();
        googleAuth.Setup(s => s.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleUserInfoDto?)null);

        var result = await AuthTestData.CreateService(googleAuthService: googleAuth)
            .GoogleLoginAsync(new GoogleLoginRequestDto(ValidIdToken), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.GoogleTokenInvalid);
    }

    [TestMethod]
    public async Task GoogleLoginAsync_WhenEmailNotVerified_ReturnsEmailNotVerified()
    {
        var googleAuth = new Mock<IGoogleAuthService>();
        googleAuth.Setup(s => s.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidGoogleUser with { EmailVerified = false });

        var result = await AuthTestData.CreateService(googleAuthService: googleAuth)
            .GoogleLoginAsync(new GoogleLoginRequestDto(ValidIdToken), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.GoogleEmailNotVerified);
    }

    [TestMethod]
    public async Task GoogleLoginAsync_WhenUserIsInactive_ReturnsInactive()
    {
        var googleAuth = new Mock<IGoogleAuthService>();
        googleAuth.Setup(s => s.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidGoogleUser);

        var account = new Mock<IAccountService>();
        account.Setup(a => a.FindByGoogleIdAsync(ValidGoogleUser.GoogleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser with { IsActive = false });

        var result = await AuthTestData.CreateService(googleAuthService: googleAuth, accountService: account)
            .GoogleLoginAsync(new GoogleLoginRequestDto(ValidIdToken), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.UserInactive);
    }

    [TestMethod]
    public async Task GoogleLoginAsync_WhenUserIsAdmin_ReturnsChallengeWithoutPersistingSession()
    {
        var googleAuth = new Mock<IGoogleAuthService>();
        googleAuth.Setup(s => s.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidGoogleUser);

        var account = new Mock<IAccountService>();
        account.Setup(a => a.FindByGoogleIdAsync(ValidGoogleUser.GoogleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(a => a.GetRolesAsync(AuthTestData.ActiveUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { AuthConstants.AdminRole });

        var cacheService = new Mock<ICacheService>();
        var emailService = new Mock<IEmailService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var repository = new Mock<IAuthTokenRepository>();

        var result = await AuthTestData.CreateService(
                googleAuthService: googleAuth,
                accountService: account,
                unitOfWork: unitOfWork,
                authTokenRepository: repository,
                cacheService: cacheService,
                emailService: emailService)
            .GoogleLoginAsync(new GoogleLoginRequestDto(ValidIdToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequiresTwoFactor.Should().BeTrue();
        result.Value.TempToken.Should().NotBeNullOrWhiteSpace();
        result.Value.TwoFactorExpiresAtUtc.Should().NotBeNull();
        result.Value.AccessToken.Should().BeNull();

        emailService.Verify(service => service.SendAsync(
            AuthTestData.ActiveUser.Email,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        cacheService.Verify(service => service.SetAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GoogleLoginAsync_WhenValidAndNotAdmin_PersistsSession()
    {
        var googleAuth = new Mock<IGoogleAuthService>();
        googleAuth.Setup(s => s.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidGoogleUser);

        var account = new Mock<IAccountService>();
        account.Setup(a => a.FindByGoogleIdAsync(ValidGoogleUser.GoogleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfoDto?)null);
        account.Setup(a => a.FindByEmailAsync(ValidGoogleUser.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(a => a.LinkGoogleIdentityAsync(
                AuthTestData.ActiveUser.Id, ValidGoogleUser.GoogleId, ValidGoogleUser.AvatarUrl, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(a => a.GetRolesAsync(AuthTestData.ActiveUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.Roles);

        var unitOfWork = new Mock<IUnitOfWork>();
        var repository = new Mock<IAuthTokenRepository>();

        var result = await AuthTestData.CreateService(
                googleAuthService: googleAuth,
                accountService: account,
                unitOfWork: unitOfWork,
                authTokenRepository: repository)
            .GoogleLoginAsync(new GoogleLoginRequestDto(ValidIdToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().NotBeNull();
        result.Value.RequiresTwoFactor.Should().BeFalse();

        repository.Verify(candidate => candidate.AddAsync(
            It.IsAny<RefreshTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2)); // Link + Session
    }
}
