using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Common.Helpers;
using FluentAssertions;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class VerifyAdminTwoFactorTests
{
    private const string ValidTempToken = "valid-temp-token";
    private const string ValidOtpCode = "123456";

    [TestMethod]
    public async Task VerifyAdminTwoFactorAsync_WhenValidationFails_ReturnsValidationError()
    {
        var validator = new Mock<FluentValidation.IValidator<AdminVerifyTwoFactorRequestDto>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<AdminVerifyTwoFactorRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FluentValidation.Results.ValidationResult(new[] { new FluentValidation.Results.ValidationFailure("Prop", "Error") }));

        var result = await AuthTestData.CreateService(
                adminVerifyTwoFactorValidator: validator.Object)
            .VerifyAdminTwoFactorAsync(new AdminVerifyTwoFactorRequestDto("", ""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [TestMethod]
    public async Task VerifyAdminTwoFactorAsync_WhenChallengeIsMissing_ReturnsInvalid()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminTwoFactorChallenge?)null);

        var result = await AuthTestData.CreateService(cacheService: cache)
            .VerifyAdminTwoFactorAsync(new AdminVerifyTwoFactorRequestDto(ValidTempToken, ValidOtpCode), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.AdminTwoFactorInvalid);
    }

    [TestMethod]
    public async Task VerifyAdminTwoFactorAsync_WhenChallengeExpired_ReturnsExpired()
    {
        var challenge = CreateChallenge(AuthTestData.UtcNow.AddMinutes(-5));
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var result = await AuthTestData.CreateService(cacheService: cache)
            .VerifyAdminTwoFactorAsync(new AdminVerifyTwoFactorRequestDto(ValidTempToken, ValidOtpCode), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.AdminTwoFactorExpired);
    }

    [TestMethod]
    public async Task VerifyAdminTwoFactorAsync_WhenOtpIsIncorrect_ReturnsInvalid()
    {
        var challenge = CreateChallenge(AuthTestData.UtcNow.AddMinutes(5));
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var result = await AuthTestData.CreateService(cacheService: cache)
            .VerifyAdminTwoFactorAsync(new AdminVerifyTwoFactorRequestDto(ValidTempToken, "000000"), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.AdminTwoFactorInvalid);
    }

    [TestMethod]
    public async Task VerifyAdminTwoFactorAsync_WhenAccountIsMissingOrInactive_ReturnsInvalid()
    {
        var challenge = CreateChallenge(AuthTestData.UtcNow.AddMinutes(5));
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var account = new Mock<IAccountService>();
        account.Setup(a => a.FindByIdAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser with { IsActive = false });

        var result = await AuthTestData.CreateService(cacheService: cache, accountService: account)
            .VerifyAdminTwoFactorAsync(new AdminVerifyTwoFactorRequestDto(ValidTempToken, ValidOtpCode), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.AdminTwoFactorInvalid);
    }

    [TestMethod]
    public async Task VerifyAdminTwoFactorAsync_WhenAccountLacksAdminRole_ReturnsInvalid()
    {
        var challenge = CreateChallenge(AuthTestData.UtcNow.AddMinutes(5));
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var account = new Mock<IAccountService>();
        account.Setup(a => a.FindByIdAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(a => a.GetRolesAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.Roles);

        var result = await AuthTestData.CreateService(cacheService: cache, accountService: account)
            .VerifyAdminTwoFactorAsync(new AdminVerifyTwoFactorRequestDto(ValidTempToken, ValidOtpCode), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.AdminTwoFactorInvalid);
    }

    [TestMethod]
    public async Task VerifyAdminTwoFactorAsync_WhenValid_IssuesSessionAndRemovesChallenge()
    {
        var challenge = CreateChallenge(AuthTestData.UtcNow.AddMinutes(5));
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var account = new Mock<IAccountService>();
        account.Setup(a => a.FindByIdAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(a => a.GetRolesAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { AuthConstants.AdminRole });

        var unitOfWork = new Mock<IUnitOfWork>();
        var repository = new Mock<IAuthTokenRepository>();

        var result = await AuthTestData.CreateService(
                cacheService: cache,
                accountService: account,
                unitOfWork: unitOfWork,
                authTokenRepository: repository)
            .VerifyAdminTwoFactorAsync(new AdminVerifyTwoFactorRequestDto(ValidTempToken, ValidOtpCode), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().NotBeNull();

        cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        account.Verify(service => service.TouchLastLoginAsync(
            AuthTestData.ActiveUser.Id, AuthTestData.UtcNow, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(candidate => candidate.AddAsync(
            It.IsAny<RefreshTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AdminTwoFactorChallenge CreateChallenge(DateTimeOffset expiresAtUtc)
    {
        return new AdminTwoFactorChallenge(
            AuthTestData.ActiveUser.Id,
            AuthTestData.ActiveUser.Email,
            AuthTestData.ActiveUser.FullName,
            HashHelper.ComputeSha256Hash($"{ValidTempToken}:{ValidOtpCode}"),
            "pending",
            expiresAtUtc,
            RequestContext.None);
    }
}
