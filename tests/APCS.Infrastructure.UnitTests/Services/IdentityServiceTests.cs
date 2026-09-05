using APCS.Common.Constants;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Services;
using APCS.Infrastructure.UnitTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class IdentityServiceTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public async Task EmailExistsAsync_WhenUserExists_ReturnsTrue()
    {
        var managers = new IdentityManagerMocks();
        managers.UserManager.Setup(manager => manager.FindByEmailAsync("user@example.com"))
            .ReturnsAsync(new User());

        var result = await CreateService(managers).EmailExistsAsync("user@example.com");

        result.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenCreateFails_ReturnsIdentityErrors()
    {
        var managers = new IdentityManagerMocks();
        managers.UserManager.Setup(manager => manager.CreateAsync(It.IsAny<User>(), "Password1"))
            .ReturnsAsync(Failed("Create failed"));

        var result = await CreateService(managers).CreateUserAsync(
            "user@example.com", "Password1", "User", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Equal("Create failed");
        result.User.Should().BeNull();
        managers.RoleManager.Verify(manager => manager.RoleExistsAsync(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenRoleExists_CreatesActiveUserAndAssignsRole()
    {
        var managers = new IdentityManagerMocks();
        User? createdUser = null;
        managers.UserManager.Setup(manager => manager.CreateAsync(It.IsAny<User>(), "Password1"))
            .Callback<User, string>((user, _) => createdUser = user)
            .ReturnsAsync(IdentityResult.Success);
        managers.RoleManager.Setup(manager => manager.RoleExistsAsync(AuthConstants.UserRole))
            .ReturnsAsync(true);
        managers.UserManager.Setup(manager => manager.AddToRoleAsync(
                It.IsAny<User>(), AuthConstants.UserRole))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService(managers).CreateUserAsync(
            "user@example.com", "Password1", "User Name", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        createdUser.Should().NotBeNull();
        createdUser!.UserName.Should().Be("user@example.com");
        createdUser.Email.Should().Be("user@example.com");
        createdUser.FullName.Should().Be("User Name");
        createdUser.EmailConfirmed.Should().BeTrue();
        createdUser.EmailVerifiedAtUtc.Should().Be(UtcNow);
        createdUser.AccountStatus.Should().Be(AccountStatuses.Active);
    }

    [TestMethod]
    public async Task CreateUserAsync_OnSuccess_ReturnsTheCreatedUserAndItsRoles()
    {
        var managers = SetupSuccessfulUserCreation();
        managers.RoleManager.Setup(manager => manager.RoleExistsAsync(AuthConstants.UserRole))
            .ReturnsAsync(true);
        managers.UserManager.Setup(manager => manager.AddToRoleAsync(
                It.IsAny<User>(), AuthConstants.UserRole))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService(managers).CreateUserAsync(
            "user@example.com", "Password1", "User Name", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User!.Email.Should().Be("user@example.com");
        result.User.FullName.Should().Be("User Name");
        result.Roles.Should().Equal(AuthConstants.UserRole);
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenRoleCreationFails_DeletesUserAndReturnsFailure()
    {
        var managers = SetupSuccessfulUserCreation();
        managers.RoleManager.Setup(manager => manager.RoleExistsAsync(AuthConstants.UserRole))
            .ReturnsAsync(false);
        managers.RoleManager.Setup(manager => manager.CreateAsync(It.IsAny<Role>()))
            .ReturnsAsync(Failed("Role failed"));
        managers.UserManager.Setup(manager => manager.DeleteAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService(managers).CreateUserAsync(
            "user@example.com", "Password1", "User", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Equal("Role failed");
        managers.UserManager.Verify(manager => manager.DeleteAsync(It.IsAny<User>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenRoleWasCreatedConcurrently_ContinuesSuccessfully()
    {
        var managers = SetupSuccessfulUserCreation();
        managers.RoleManager.SetupSequence(manager => manager.RoleExistsAsync(AuthConstants.UserRole))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        managers.RoleManager.Setup(manager => manager.CreateAsync(It.IsAny<Role>()))
            .ReturnsAsync(Failed("Duplicate role"));
        managers.UserManager.Setup(manager => manager.AddToRoleAsync(
                It.IsAny<User>(), AuthConstants.UserRole))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService(managers).CreateUserAsync(
            "user@example.com", "Password1", "User", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenRoleAssignmentFails_DeletesUserAndReturnsFailure()
    {
        var managers = SetupSuccessfulUserCreation();
        managers.RoleManager.Setup(manager => manager.RoleExistsAsync(AuthConstants.UserRole))
            .ReturnsAsync(true);
        managers.UserManager.Setup(manager => manager.AddToRoleAsync(
                It.IsAny<User>(), AuthConstants.UserRole))
            .ReturnsAsync(Failed("Assignment failed"));
        managers.UserManager.Setup(manager => manager.DeleteAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService(managers).CreateUserAsync(
            "user@example.com", "Password1", "User", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Equal("Assignment failed");
        managers.UserManager.Verify(manager => manager.DeleteAsync(It.IsAny<User>()), Times.Once);
    }

    [TestMethod]
    public async Task FindByEmailAsync_WhenUserExists_MapsStatusAndLockout()
    {
        var managers = new IdentityManagerMocks();
        var user = CreateUser();
        managers.UserManager.Setup(manager => manager.FindByEmailAsync("user@example.com"))
            .ReturnsAsync(user);
        managers.UserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(true);

        var result = await CreateService(managers).FindByEmailAsync("user@example.com");

        result.Should().NotBeNull();
        result!.Id.Should().Be(UserId);
        result.Email.Should().Be("user@example.com");
        result.FullName.Should().Be("User Name");
        result.IsActive.Should().BeTrue();
        result.IsLockedOut.Should().BeTrue();
    }

    [TestMethod]
    public async Task LookupsForTheSameUser_HitTheStoreOnlyOnce()
    {
        var managers = new IdentityManagerMocks();
        var user = CreateUser();
        managers.UserManager.Setup(manager => manager.FindByEmailAsync("user@example.com"))
            .ReturnsAsync(user);
        managers.UserManager.Setup(manager => manager.GetRolesAsync(user)).ReturnsAsync(["user"]);
        managers.UserManager.Setup(manager => manager.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        var service = CreateService(managers);

        await service.FindByEmailAsync("user@example.com");
        await service.FindByIdAsync(UserId);
        await service.GetRolesAsync(UserId);
        await service.TouchLastLoginAsync(UserId, UtcNow);

        managers.UserManager.Verify(manager => manager.FindByEmailAsync("user@example.com"), Times.Once);
        managers.UserManager.Verify(manager => manager.FindByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_WhenUserMissing_ReturnsRejectedResult()
    {
        var managers = new IdentityManagerMocks();
        managers.UserManager.Setup(manager => manager.FindByIdAsync(UserId.ToString()))
            .ReturnsAsync((User?)null);

        var result = await CreateService(managers).ValidateCredentialsAsync(UserId, "Password1");

        result.Succeeded.Should().BeFalse();
        result.IsLockedOut.Should().BeFalse();
        result.IsNotAllowed.Should().BeFalse();
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_WhenSignInLocksUser_MapsSignInResult()
    {
        var managers = new IdentityManagerMocks();
        var user = CreateUser();
        managers.UserManager.Setup(manager => manager.FindByIdAsync(UserId.ToString())).ReturnsAsync(user);
        managers.SignInManager.Setup(manager => manager.CheckPasswordSignInAsync(
                user, "Password1", true))
            .ReturnsAsync(SignInResult.LockedOut);

        var result = await CreateService(managers).ValidateCredentialsAsync(UserId, "Password1");

        result.Succeeded.Should().BeFalse();
        result.IsLockedOut.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetRolesAsync_ForExistingAndMissingUsers_ReturnsExpectedRoles()
    {
        var managers = new IdentityManagerMocks();
        var user = CreateUser();
        managers.UserManager.Setup(manager => manager.FindByIdAsync(UserId.ToString())).ReturnsAsync(user);
        managers.UserManager.Setup(manager => manager.GetRolesAsync(user))
            .ReturnsAsync(["user", "admin"]);
        managers.UserManager.Setup(manager => manager.FindByIdAsync(OtherUserId.ToString()))
            .ReturnsAsync((User?)null);
        var service = CreateService(managers);

        (await service.GetRolesAsync(UserId)).Should().Equal("user", "admin");
        (await service.GetRolesAsync(OtherUserId)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task TouchLastLoginAsync_WhenUserExists_UpdatesLastLogin()
    {
        var managers = new IdentityManagerMocks();
        var user = CreateUser();
        managers.UserManager.Setup(manager => manager.FindByIdAsync(UserId.ToString())).ReturnsAsync(user);
        managers.UserManager.Setup(manager => manager.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        await CreateService(managers).TouchLastLoginAsync(UserId, UtcNow);

        // UpdatedAtUtc is stamped by the persistence layer on save, not here.
        user.LastLoginAtUtc.Should().Be(UtcNow);
        managers.UserManager.Verify(manager => manager.UpdateAsync(user), Times.Once);
    }

    [TestMethod]
    public async Task PublicOperation_WhenCancellationRequested_ThrowsBeforeCallingIdentity()
    {
        var managers = new IdentityManagerMocks();
        using var source = new CancellationTokenSource();
        source.Cancel();

        var act = () => CreateService(managers).EmailExistsAsync("user@example.com", source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        managers.UserManager.Verify(manager => manager.FindByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    private static IdentityService CreateService(IdentityManagerMocks managers) => new(
        managers.UserManager.Object,
        managers.SignInManager.Object,
        managers.RoleManager.Object,
        new FakeTimeProvider(UtcNow));

    private static IdentityManagerMocks SetupSuccessfulUserCreation()
    {
        var managers = new IdentityManagerMocks();
        managers.UserManager.Setup(manager => manager.CreateAsync(It.IsAny<User>(), "Password1"))
            .ReturnsAsync(IdentityResult.Success);
        return managers;
    }

    private static User CreateUser() => new()
    {
        Id = UserId,
        UserName = "user@example.com",
        Email = "user@example.com",
        FullName = "User Name",
        AccountStatus = AccountStatuses.Active
    };

    private static IdentityResult Failed(string description) => IdentityResult.Failed(
        new IdentityError { Code = "unit_test", Description = description });
}
