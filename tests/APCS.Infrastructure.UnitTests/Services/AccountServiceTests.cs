using APCS.Application.Abstractions.Persistence;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class AccountServiceTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Email = "seller@example.com";
    private const string GoogleId = "google-subject-id";

    private static User CreateUser(
        Guid? id = null,
        string email = Email,
        string? passwordHash = "hashed-password",
        string accountStatus = AccountStatuses.Active,
        bool? emailVerified = true,
        string? oauthGoogleId = null,
        string? oauthProvider = null,
        string? avatarUrl = null) => new()
    {
        Id = id ?? UserId,
        Email = email,
        FullName = "Seller Name",
        PasswordHash = passwordHash,
        AccountStatus = accountStatus,
        EmailVerified = emailVerified,
        OauthGoogleId = oauthGoogleId,
        OauthProvider = oauthProvider,
        AvatarUrl = avatarUrl,
        CreatedAt = UtcNow.UtcDateTime,
        UpdatedAt = UtcNow.UtcDateTime
    };

    private static (AccountService Service, Mock<IAccountRepository> Repository, Mock<IPasswordHasher<User>> Hasher)
        CreateService(TimeProvider? timeProvider = null)
    {
        var repository = new Mock<IAccountRepository>();
        var hasher = new Mock<IPasswordHasher<User>>();
        var service = new AccountService(repository.Object, hasher.Object, timeProvider ?? new FakeTimeProvider(UtcNow));
        return (service, repository, hasher);
    }

    // ── FindByGoogleIdAsync ─────────────────────────────────────────

    [TestMethod]
    public async Task FindByGoogleIdAsync_WhenFound_ReturnsMappedAccount()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.FindByGoogleIdAsync(GoogleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateUser(oauthGoogleId: GoogleId));

        var result = await service.FindByGoogleIdAsync(GoogleId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(UserId);
    }

    [TestMethod]
    public async Task FindByGoogleIdAsync_WhenNotFound_ReturnsNull()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.FindByGoogleIdAsync(GoogleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await service.FindByGoogleIdAsync(GoogleId);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task FindByGoogleIdAsync_WhenAlreadyCachedFromEarlierLookup_DoesNotQueryAgain()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateUser(oauthGoogleId: GoogleId));

        await service.FindByEmailAsync(Email);
        var result = await service.FindByGoogleIdAsync(GoogleId);

        result.Should().NotBeNull();
        repository.Verify(r => r.FindByGoogleIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task FindByGoogleIdAsync_WithBlankGoogleId_Throws()
    {
        var (service, _, _) = CreateService();

        var act = async () => await service.FindByGoogleIdAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── CreateGoogleUserAsync ───────────────────────────────────────

    [TestMethod]
    public async Task CreateGoogleUserAsync_WhenEmailAlreadyExists_ReturnsFailure()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.EmailExistsAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await service.CreateGoogleUserAsync(Email, "Seller Name", GoogleId, null);

        result.Succeeded.Should().BeFalse();
        result.User.Should().BeNull();
    }

    [TestMethod]
    public async Task CreateGoogleUserAsync_WhenDefaultRoleExists_ReusesItWithoutCreatingANewOne()
    {
        var (service, repository, _) = CreateService();
        var existingRole = new Role { Id = Guid.NewGuid(), Code = AuthConstants.UserRole };
        repository.Setup(r => r.EmailExistsAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.Setup(r => r.FindRoleByCodeAsync(AuthConstants.UserRole, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRole);

        var result = await service.CreateGoogleUserAsync(Email, "Seller Name", GoogleId, "https://avatar");

        result.Succeeded.Should().BeTrue();
        result.User!.Email.Should().Be(Email);
        result.Roles.Should().ContainSingle().Which.Should().Be(AuthConstants.UserRole);
        repository.Verify(r => r.AddRole(It.IsAny<Role>()), Times.Never);
        repository.Verify(
            r => r.AddUserRole(It.Is<UserRole>(userRole => userRole.RoleId == existingRole.Id)),
            Times.Once);
        repository.Verify(
            r => r.AddAsync(
                It.Is<User>(user =>
                    user.Email == Email
                    && user.PasswordHash == null
                    && user.OauthGoogleId == GoogleId
                    && user.OauthProvider == AuthConstants.GoogleProvider
                    && user.AvatarUrl == "https://avatar"
                    && user.AccountStatus == AccountStatuses.Active
                    && user.EmailVerified == true),
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CreateGoogleUserAsync_WhenDefaultRoleIsMissing_CreatesAndAddsIt()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.EmailExistsAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.Setup(r => r.FindRoleByCodeAsync(AuthConstants.UserRole, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        var result = await service.CreateGoogleUserAsync(Email, "Seller Name", GoogleId, null);

        result.Succeeded.Should().BeTrue();
        repository.Verify(
            r => r.AddRole(It.Is<Role>(role => role.Code == AuthConstants.UserRole && role.IsSystemRole == true)),
            Times.Once);
    }

    [TestMethod]
    [DataRow("", "Seller Name", GoogleId)]
    [DataRow(Email, "", GoogleId)]
    [DataRow(Email, "Seller Name", "")]
    public async Task CreateGoogleUserAsync_WithBlankRequiredArgument_Throws(
        string email,
        string fullName,
        string googleId)
    {
        var (service, _, _) = CreateService();

        var act = async () => await service.CreateGoogleUserAsync(email, fullName, googleId, null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── LinkGoogleIdentityAsync ─────────────────────────────────────

    [TestMethod]
    public async Task LinkGoogleIdentityAsync_WhenUserNotFound_ReturnsNull()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await service.LinkGoogleIdentityAsync(UserId, GoogleId, null, UtcNow);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task LinkGoogleIdentityAsync_WhenAccountAlreadyVerified_LinksIdentityWithoutTouchingVerificationState()
    {
        var (service, repository, _) = CreateService();
        var user = CreateUser(accountStatus: AccountStatuses.Active, emailVerified: true);
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await service.LinkGoogleIdentityAsync(UserId, GoogleId, "https://avatar", UtcNow);

        result.Should().NotBeNull();
        user.OauthGoogleId.Should().Be(GoogleId);
        user.OauthProvider.Should().Be(AuthConstants.GoogleProvider);
        user.AvatarUrl.Should().Be("https://avatar");
        // Only a first-time confirmation clears the password (see the pre-hijacking test below);
        // an already-verified owner's existing password must survive linking a second sign-in
        // method.
        user.PasswordHash.Should().NotBeNull();
    }

    [TestMethod]
    public async Task LinkGoogleIdentityAsync_WhenProviderAndAvatarAlreadySet_DoesNotOverwriteThem()
    {
        var (service, repository, _) = CreateService();
        var user = CreateUser(oauthProvider: "existing-provider", avatarUrl: "https://existing-avatar");
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await service.LinkGoogleIdentityAsync(UserId, GoogleId, "https://new-avatar", UtcNow);

        user.OauthProvider.Should().Be("existing-provider");
        user.AvatarUrl.Should().Be("https://existing-avatar");
    }

    [TestMethod]
    public async Task LinkGoogleIdentityAsync_WhenAccountPendingVerification_ConfirmsEmailAndActivates()
    {
        var (service, repository, _) = CreateService();
        var user = CreateUser(accountStatus: AccountStatuses.PendingVerification, emailVerified: false);
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await service.LinkGoogleIdentityAsync(UserId, GoogleId, null, UtcNow);

        user.EmailVerified.Should().BeTrue();
        user.EmailVerifiedAt.Should().Be(UtcNow.UtcDateTime);
        user.AccountStatus.Should().Be(AccountStatuses.Active);
        user.PasswordHash.Should().BeNull();
    }

    [TestMethod]
    public async Task LinkGoogleIdentityAsync_WhenAccountLockedAndUnverified_ConfirmsEmailButKeepsLock()
    {
        var (service, repository, _) = CreateService();
        var user = CreateUser(accountStatus: AccountStatuses.Locked, emailVerified: false);
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await service.LinkGoogleIdentityAsync(UserId, GoogleId, null, UtcNow);

        user.EmailVerified.Should().BeTrue();
        user.AccountStatus.Should().Be(AccountStatuses.Locked);
    }

    [TestMethod]
    public async Task LinkGoogleIdentityAsync_WhenAccountWasNeverVerified_ClearsAnyPreRegisteredPassword()
    {
        // Regression test for a pre-hijacking gap: an attacker who registers the victim's email
        // with an attacker-known password, then never verifies it, must not retain sign-in access
        // once the real owner claims the account through Google.
        var (service, repository, _) = CreateService();
        var user = CreateUser(
            accountStatus: AccountStatuses.PendingVerification,
            emailVerified: false,
            passwordHash: "attacker-chosen-hash");
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await service.LinkGoogleIdentityAsync(UserId, GoogleId, null, UtcNow);

        user.PasswordHash.Should().BeNull();
    }

    [TestMethod]
    public async Task LinkGoogleIdentityAsync_WithBlankGoogleId_Throws()
    {
        var (service, _, _) = CreateService();

        var act = async () => await service.LinkGoogleIdentityAsync(UserId, " ", null, UtcNow);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── EmailExistsAsync ────────────────────────────────────────────

    [TestMethod]
    public async Task EmailExistsAsync_NormalizesEmailBeforeDelegatingToRepository()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.EmailExistsAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await service.EmailExistsAsync("  Seller@Example.com  ");

        result.Should().BeTrue();
        repository.Verify(r => r.EmailExistsAsync(Email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task EmailExistsAsync_WithBlankEmail_Throws()
    {
        var (service, _, _) = CreateService();

        var act = async () => await service.EmailExistsAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── CreateUserAsync ─────────────────────────────────────────────

    [TestMethod]
    public async Task CreateUserAsync_WhenEmailAlreadyExists_ReturnsFailure()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.EmailExistsAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await service.CreateUserAsync(Email, "Password1", "Seller Name");

        result.Succeeded.Should().BeFalse();
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenDefaultRoleExists_CreatesPendingAccountAndReusesRole()
    {
        var (service, repository, hasher) = CreateService();
        var existingRole = new Role { Id = Guid.NewGuid(), Code = AuthConstants.UserRole };
        repository.Setup(r => r.EmailExistsAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.Setup(r => r.FindRoleByCodeAsync(AuthConstants.UserRole, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRole);
        hasher.Setup(h => h.HashPassword(It.IsAny<User>(), "Password1")).Returns("hashed");

        var result = await service.CreateUserAsync(Email, "Password1", "Seller Name");

        result.Succeeded.Should().BeTrue();
        repository.Verify(r => r.AddRole(It.IsAny<Role>()), Times.Never);
        repository.Verify(
            r => r.AddAsync(
                It.Is<User>(user =>
                    user.AccountStatus == AccountStatuses.PendingVerification
                    && user.EmailVerified == false
                    && user.PasswordHash == "hashed"),
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenDefaultRoleIsMissing_CreatesAndAddsIt()
    {
        var (service, repository, hasher) = CreateService();
        repository.Setup(r => r.EmailExistsAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.Setup(r => r.FindRoleByCodeAsync(AuthConstants.UserRole, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);
        hasher.Setup(h => h.HashPassword(It.IsAny<User>(), It.IsAny<string>())).Returns("hashed");

        var result = await service.CreateUserAsync(Email, "Password1", "Seller Name");

        result.Succeeded.Should().BeTrue();
        repository.Verify(r => r.AddRole(It.Is<Role>(role => role.Code == AuthConstants.UserRole)), Times.Once);
    }

    [TestMethod]
    [DataRow("", "Password1", "Seller Name")]
    [DataRow(Email, "", "Seller Name")]
    [DataRow(Email, "Password1", "")]
    public async Task CreateUserAsync_WithBlankRequiredArgument_Throws(
        string email,
        string password,
        string fullName)
    {
        var (service, _, _) = CreateService();

        var act = async () => await service.CreateUserAsync(email, password, fullName);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── FindByEmailAsync ────────────────────────────────────────────

    [TestMethod]
    public async Task FindByEmailAsync_WhenFound_ReturnsMappedAccount()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.FindByEmailAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(CreateUser());

        var result = await service.FindByEmailAsync(Email);

        result.Should().NotBeNull();
        result!.Email.Should().Be(Email);
    }

    [TestMethod]
    public async Task FindByEmailAsync_WhenNotFound_ReturnsNull()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.FindByEmailAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await service.FindByEmailAsync(Email);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task FindByEmailAsync_WhenAlreadyCachedForSameEmail_DoesNotQueryAgain()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.FindByEmailAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(CreateUser());

        await service.FindByEmailAsync(Email);
        await service.FindByEmailAsync(Email);

        repository.Verify(r => r.FindByEmailAsync(Email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task FindByEmailAsync_WithBlankEmail_Throws()
    {
        var (service, _, _) = CreateService();

        var act = async () => await service.FindByEmailAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── FindByIdAsync ───────────────────────────────────────────────

    [TestMethod]
    public async Task FindByIdAsync_WhenFound_ReturnsMappedAccount()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateUser());

        var result = await service.FindByIdAsync(UserId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(UserId);
    }

    [TestMethod]
    public async Task FindByIdAsync_WhenNotFound_ReturnsNull()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await service.FindByIdAsync(UserId);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task FindByIdAsync_WhenAlreadyCachedForSameId_DoesNotQueryAgain()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateUser());

        await service.FindByIdAsync(UserId);
        await service.FindByIdAsync(UserId);

        repository.Verify(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ValidateCredentialsAsync ────────────────────────────────────

    [TestMethod]
    public async Task ValidateCredentialsAsync_WhenHashMatches_ReturnsTrue()
    {
        var (service, repository, hasher) = CreateService();
        var user = CreateUser();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        hasher.Setup(h => h.VerifyHashedPassword(user, user.PasswordHash!, "Password1"))
            .Returns(PasswordVerificationResult.Success);

        var result = await service.ValidateCredentialsAsync(UserId, "Password1");

        result.Should().BeTrue();
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_WhenHashDoesNotMatch_ReturnsFalse()
    {
        var (service, repository, hasher) = CreateService();
        var user = CreateUser();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        hasher.Setup(h => h.VerifyHashedPassword(user, user.PasswordHash!, "wrong"))
            .Returns(PasswordVerificationResult.Failed);

        var result = await service.ValidateCredentialsAsync(UserId, "wrong");

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_WhenUserNotFound_ReturnsFalse()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await service.ValidateCredentialsAsync(UserId, "Password1");

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_WhenAccountHasNoPassword_ReturnsFalse()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateUser(passwordHash: null));

        var result = await service.ValidateCredentialsAsync(UserId, "Password1");

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_ReusesTheCachedUserFromAnEarlierLookup()
    {
        var (service, repository, hasher) = CreateService();
        var user = CreateUser();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        hasher.Setup(h => h.VerifyHashedPassword(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PasswordVerificationResult.Success);

        await service.FindByIdAsync(UserId);
        await service.ValidateCredentialsAsync(UserId, "Password1");

        repository.Verify(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_WithBlankPassword_Throws()
    {
        var (service, _, _) = CreateService();

        var act = async () => await service.ValidateCredentialsAsync(UserId, " ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_WithCancelledToken_Throws()
    {
        var (service, _, _) = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.ValidateCredentialsAsync(UserId, "Password1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── GetRolesAsync ───────────────────────────────────────────────

    [TestMethod]
    public async Task GetRolesAsync_DelegatesToRepository()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetActiveRoleCodesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Seller"]);

        var roles = await service.GetRolesAsync(UserId);

        roles.Should().ContainSingle().Which.Should().Be("Seller");
    }

    // ── TouchLastLoginAsync ─────────────────────────────────────────

    [TestMethod]
    public async Task TouchLastLoginAsync_AlwaysUpdatesTheRepository()
    {
        var (service, repository, _) = CreateService();

        await service.TouchLastLoginAsync(UserId, UtcNow);

        repository.Verify(r => r.TouchLastLoginAsync(UserId, UtcNow, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task TouchLastLoginAsync_WhenUserIsCached_AlsoUpdatesTheCachedCopy()
    {
        var (service, repository, _) = CreateService();
        var user = CreateUser();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await service.FindByIdAsync(UserId);
        await service.TouchLastLoginAsync(UserId, UtcNow.AddMinutes(5));

        user.LastLoginAt.Should().Be(UtcNow.AddMinutes(5).UtcDateTime);
    }

    [TestMethod]
    public async Task TouchLastLoginAsync_WhenCachedUserIsSomeoneElse_LeavesTheCacheUntouched()
    {
        var (service, repository, _) = CreateService();
        var otherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var cachedUser = CreateUser(id: otherUserId);
        repository.Setup(r => r.GetByIdAsync(otherUserId, It.IsAny<CancellationToken>())).ReturnsAsync(cachedUser);

        await service.FindByIdAsync(otherUserId);
        await service.TouchLastLoginAsync(UserId, UtcNow);

        cachedUser.LastLoginAt.Should().BeNull();
    }

    // ── ConfirmEmailAsync ───────────────────────────────────────────

    [TestMethod]
    public async Task ConfirmEmailAsync_WhenUserNotFound_ReturnsFalse()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await service.ConfirmEmailAsync(UserId, UtcNow);

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task ConfirmEmailAsync_WhenPendingVerification_ActivatesTheAccount()
    {
        var (service, repository, _) = CreateService();
        var user = CreateUser(accountStatus: AccountStatuses.PendingVerification, emailVerified: false);
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await service.ConfirmEmailAsync(UserId, UtcNow);

        result.Should().BeTrue();
        user.EmailVerified.Should().BeTrue();
        user.AccountStatus.Should().Be(AccountStatuses.Active);
    }

    [TestMethod]
    public async Task ConfirmEmailAsync_WhenAccountAdministrativelyLocked_ConfirmsEmailButLeavesTheLockInPlace()
    {
        var (service, repository, _) = CreateService();
        var user = CreateUser(accountStatus: AccountStatuses.Locked, emailVerified: false);
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await service.ConfirmEmailAsync(UserId, UtcNow);

        result.Should().BeTrue();
        user.EmailVerified.Should().BeTrue();
        user.AccountStatus.Should().Be(AccountStatuses.Locked);
    }

    // ── UpdatePasswordAsync ─────────────────────────────────────────

    [TestMethod]
    public async Task UpdatePasswordAsync_WhenUserNotFound_ReturnsFalse()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await service.UpdatePasswordAsync(UserId, "NewPassword1");

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task UpdatePasswordAsync_WhenUserFound_RehashesAndStagesTheNewPassword()
    {
        var (service, repository, hasher) = CreateService();
        var user = CreateUser();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        hasher.Setup(h => h.HashPassword(user, "NewPassword1")).Returns("new-hash");

        var result = await service.UpdatePasswordAsync(UserId, "NewPassword1");

        result.Should().BeTrue();
        user.PasswordHash.Should().Be("new-hash");
        user.UpdatedAt.Should().Be(UtcNow.UtcDateTime);
    }

    [TestMethod]
    public async Task UpdatePasswordAsync_WithBlankNewPassword_Throws()
    {
        var (service, _, _) = CreateService();

        var act = async () => await service.UpdatePasswordAsync(UserId, " ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── VerifyPasswordAsync ─────────────────────────────────────────

    [TestMethod]
    public async Task VerifyPasswordAsync_WhenHashMatches_ReturnsTrue()
    {
        var (service, repository, hasher) = CreateService();
        var user = CreateUser();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        hasher.Setup(h => h.VerifyHashedPassword(user, user.PasswordHash!, "Password1"))
            .Returns(PasswordVerificationResult.Success);

        var result = await service.VerifyPasswordAsync(UserId, "Password1");

        result.Should().BeTrue();
    }

    [TestMethod]
    public async Task VerifyPasswordAsync_WhenHashDoesNotMatch_ReturnsFalse()
    {
        var (service, repository, hasher) = CreateService();
        var user = CreateUser();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        hasher.Setup(h => h.VerifyHashedPassword(user, user.PasswordHash!, "wrong"))
            .Returns(PasswordVerificationResult.Failed);

        var result = await service.VerifyPasswordAsync(UserId, "wrong");

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task VerifyPasswordAsync_WhenUserNotFound_ReturnsFalse()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await service.VerifyPasswordAsync(UserId, "Password1");

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task VerifyPasswordAsync_WhenAccountHasNoPassword_ReturnsFalse()
    {
        var (service, repository, _) = CreateService();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateUser(passwordHash: null));

        var result = await service.VerifyPasswordAsync(UserId, "Password1");

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task VerifyPasswordAsync_ReusesTheCachedUserFromAnEarlierLookup()
    {
        var (service, repository, hasher) = CreateService();
        var user = CreateUser();
        repository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        hasher.Setup(h => h.VerifyHashedPassword(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PasswordVerificationResult.Success);

        await service.FindByIdAsync(UserId);
        await service.VerifyPasswordAsync(UserId, "Password1");

        repository.Verify(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task VerifyPasswordAsync_WithBlankCandidatePassword_Throws()
    {
        var (service, _, _) = CreateService();

        var act = async () => await service.VerifyPasswordAsync(UserId, " ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
