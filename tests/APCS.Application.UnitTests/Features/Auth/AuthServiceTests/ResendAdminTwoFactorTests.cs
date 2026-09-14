using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Email;
using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class ResendAdminTwoFactorTests
{
    private const string ValidTempToken = "valid-temp-token";

    [TestMethod]
    public async Task ResendAdminTwoFactorAsync_WhenValidationFails_ReturnsValidationError()
    {
        var validator = new Mock<FluentValidation.IValidator<AdminResendTwoFactorRequestDto>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<AdminResendTwoFactorRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FluentValidation.Results.ValidationResult(new[] { new FluentValidation.Results.ValidationFailure("Prop", "Error") }));

        var result = await AuthTestData.CreateService(
                adminResendTwoFactorValidator: validator.Object)
            .ResendAdminTwoFactorAsync(new AdminResendTwoFactorRequestDto(""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [TestMethod]
    public async Task ResendAdminTwoFactorAsync_WhenChallengeIsMissing_ReturnsInvalid()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminTwoFactorChallenge?)null);

        var result = await AuthTestData.CreateService(cacheService: cache)
            .ResendAdminTwoFactorAsync(new AdminResendTwoFactorRequestDto(ValidTempToken), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.AdminTwoFactorInvalid);
    }

    [TestMethod]
    public async Task ResendAdminTwoFactorAsync_WhenAccountIsMissingOrInactive_ReturnsInvalid()
    {
        var challenge = CreateChallenge();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var account = new Mock<IAccountService>();
        account.Setup(a => a.FindByIdAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser with { IsActive = false });

        var result = await AuthTestData.CreateService(cacheService: cache, accountService: account)
            .ResendAdminTwoFactorAsync(new AdminResendTwoFactorRequestDto(ValidTempToken), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.AdminTwoFactorInvalid);
    }

    [TestMethod]
    public async Task ResendAdminTwoFactorAsync_WhenAccountLacksAdminRole_ReturnsInvalid()
    {
        var challenge = CreateChallenge();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var account = new Mock<IAccountService>();
        account.Setup(a => a.FindByIdAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(a => a.GetRolesAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.Roles); // Not admin

        var result = await AuthTestData.CreateService(cacheService: cache, accountService: account)
            .ResendAdminTwoFactorAsync(new AdminResendTwoFactorRequestDto(ValidTempToken), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.AdminTwoFactorInvalid);
    }

    [TestMethod]
    public async Task ResendAdminTwoFactorAsync_WhenValid_SendsNewOtpAndUpdatesChallenge()
    {
        var challenge = CreateChallenge();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AdminTwoFactorChallenge>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var account = new Mock<IAccountService>();
        account.Setup(a => a.FindByIdAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(a => a.GetRolesAsync(challenge.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { AuthConstants.AdminRole });
            
        var emailService = new Mock<IEmailService>();

        var result = await AuthTestData.CreateService(
                cacheService: cache, 
                accountService: account,
                emailService: emailService)
            .ResendAdminTwoFactorAsync(new AdminResendTwoFactorRequestDto(ValidTempToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TempToken.Should().Be(ValidTempToken);
        result.Value.ExpiresAtUtc.Should().NotBe(challenge.ExpiresAtUtc); // Should be a new expiration

        cache.Verify(c => c.SetAsync(
            It.IsAny<string>(), 
            It.IsAny<AdminTwoFactorChallenge>(), 
            It.IsAny<TimeSpan?>(), 
            It.IsAny<CancellationToken>()), Times.Once);
            
        emailService.Verify(e => e.SendAsync(
            AuthTestData.ActiveUser.Email,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AdminTwoFactorChallenge CreateChallenge()
    {
        return new AdminTwoFactorChallenge(
            AuthTestData.ActiveUser.Id,
            AuthTestData.ActiveUser.Email,
            AuthTestData.ActiveUser.FullName,
            "old-hash",
            "pending",
            AuthTestData.UtcNow.AddMinutes(1),
            RequestContext.None);
    }
}
