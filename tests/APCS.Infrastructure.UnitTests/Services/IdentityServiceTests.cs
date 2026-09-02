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

    [TestMethod]
    public async Task EmailExistsAsync_WhenUserExists_ReturnsTrue()
    {
        var managers = new IdentityManagerMocks();
        managers.UserManager.Setup(manager => manager.FindByEmailAsync("seller@example.com"))
            .ReturnsAsync(new Seller());

        var result = await CreateService(managers).EmailExistsAsync("seller@example.com");

        result.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenCreateFails_ReturnsIdentityErrors()
    {
        var managers = new IdentityManagerMocks();
        managers.UserManager.Setup(manager => manager.CreateAsync(It.IsAny<Seller>(), "Password1"))
            .ReturnsAsync(Failed("Create failed"));

        var result = await CreateService(managers).CreateUserAsync(
            "seller@example.com", "Password1", "Seller", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Equal("Create failed");
        managers.RoleManager.Verify(manager => manager.RoleExistsAsync(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenRoleExists_CreatesActiveSellerAndAssignsRole()
    {
        var managers = new IdentityManagerMocks();
        Seller? createdUser = null;
        managers.UserManager.Setup(manager => manager.CreateAsync(It.IsAny<Seller>(), "Password1"))
            .Callback<Seller, string>((seller, _) => createdUser = seller)
            .ReturnsAsync(IdentityResult.Success);
        managers.RoleManager.Setup(manager => manager.RoleExistsAsync(AuthConstants.UserRole))
            .ReturnsAsync(true);
        managers.UserManager.Setup(manager => manager.AddToRoleAsync(
                It.IsAny<Seller>(), AuthConstants.UserRole))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService(managers).CreateUserAsync(
            "seller@example.com", "Password1", "Seller Name", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        createdUser.Should().NotBeNull();
        createdUser!.UserName.Should().Be("seller@example.com");
        createdUser.Email.Should().Be("seller@example.com");
        createdUser.FullName.Should().Be("Seller Name");
        createdUser.EmailConfirmed.Should().BeTrue();
        createdUser.EmailVerifiedAtUtc.Should().Be(UtcNow);
        createdUser.AccountStatus.Should().Be("active");
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenRoleCreationFails_DeletesUserAndReturnsFailure()
    {
        var managers = SetupSuccessfulUserCreation();
        managers.RoleManager.Setup(manager => manager.RoleExistsAsync(AuthConstants.UserRole))
            .ReturnsAsync(false);
        managers.RoleManager.Setup(manager => manager.CreateAsync(It.IsAny<IdentityRole<int>>()))
            .ReturnsAsync(Failed("Role failed"));
        managers.UserManager.Setup(manager => manager.DeleteAsync(It.IsAny<Seller>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService(managers).CreateUserAsync(
            "seller@example.com", "Password1", "Seller", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Equal("Role failed");
        managers.UserManager.Verify(manager => manager.DeleteAsync(It.IsAny<Seller>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenRoleWasCreatedConcurrently_ContinuesSuccessfully()
    {
        var managers = SetupSuccessfulUserCreation();
        managers.RoleManager.SetupSequence(manager => manager.RoleExistsAsync(AuthConstants.UserRole))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        managers.RoleManager.Setup(manager => manager.CreateAsync(It.IsAny<IdentityRole<int>>()))
            .ReturnsAsync(Failed("Duplicate role"));
        managers.UserManager.Setup(manager => manager.AddToRoleAsync(
                It.IsAny<Seller>(), AuthConstants.UserRole))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService(managers).CreateUserAsync(
            "seller@example.com", "Password1", "Seller", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateUserAsync_WhenRoleAssignmentFails_DeletesUserAndReturnsFailure()
    {
        var managers = SetupSuccessfulUserCreation();
        managers.RoleManager.Setup(manager => manager.RoleExistsAsync(AuthConstants.UserRole))
            .ReturnsAsync(true);
        managers.UserManager.Setup(manager => manager.AddToRoleAsync(
                It.IsAny<Seller>(), AuthConstants.UserRole))
            .ReturnsAsync(Failed("Assignment failed"));
        managers.UserManager.Setup(manager => manager.DeleteAsync(It.IsAny<Seller>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService(managers).CreateUserAsync(
            "seller@example.com", "Password1", "Seller", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Equal("Assignment failed");
        managers.UserManager.Verify(manager => manager.DeleteAsync(It.IsAny<Seller>()), Times.Once);
    }

    [TestMethod]
    public async Task FindByEmailAsync_WhenUserExists_MapsStatusAndLockout()
    {
        var managers = new IdentityManagerMocks();
        var seller = CreateSeller();
        managers.UserManager.Setup(manager => manager.FindByEmailAsync("seller@example.com"))
            .ReturnsAsync(seller);
        managers.UserManager.Setup(manager => manager.IsLockedOutAsync(seller)).ReturnsAsync(true);

        var result = await CreateService(managers).FindByEmailAsync("seller@example.com");

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.Email.Should().Be("seller@example.com");
        result.FullName.Should().Be("Seller Name");
        result.IsActive.Should().BeTrue();
        result.IsLockedOut.Should().BeTrue();
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_WhenUserMissing_ReturnsRejectedResult()
    {
        var managers = new IdentityManagerMocks();
        managers.UserManager.Setup(manager => manager.FindByIdAsync("42"))
            .ReturnsAsync((Seller?)null);

        var result = await CreateService(managers).ValidateCredentialsAsync(42, "Password1");

        result.Succeeded.Should().BeFalse();
        result.IsLockedOut.Should().BeFalse();
        result.IsNotAllowed.Should().BeFalse();
    }

    [TestMethod]
    public async Task ValidateCredentialsAsync_WhenSignInLocksUser_MapsSignInResult()
    {
        var managers = new IdentityManagerMocks();
        var seller = CreateSeller();
        managers.UserManager.Setup(manager => manager.FindByIdAsync("42")).ReturnsAsync(seller);
        managers.SignInManager.Setup(manager => manager.CheckPasswordSignInAsync(
                seller, "Password1", true))
            .ReturnsAsync(SignInResult.LockedOut);

        var result = await CreateService(managers).ValidateCredentialsAsync(42, "Password1");

        result.Succeeded.Should().BeFalse();
        result.IsLockedOut.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetRolesAsync_ForExistingAndMissingUsers_ReturnsExpectedRoles()
    {
        var managers = new IdentityManagerMocks();
        var seller = CreateSeller();
        managers.UserManager.Setup(manager => manager.FindByIdAsync("42")).ReturnsAsync(seller);
        managers.UserManager.Setup(manager => manager.GetRolesAsync(seller))
            .ReturnsAsync(["user", "admin"]);
        managers.UserManager.Setup(manager => manager.FindByIdAsync("7")).ReturnsAsync((Seller?)null);
        var service = CreateService(managers);

        (await service.GetRolesAsync(42)).Should().Equal("user", "admin");
        (await service.GetRolesAsync(7)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task TouchLastLoginAsync_WhenUserExists_UpdatesTimestamps()
    {
        var managers = new IdentityManagerMocks();
        var seller = CreateSeller();
        managers.UserManager.Setup(manager => manager.FindByIdAsync("42")).ReturnsAsync(seller);
        managers.UserManager.Setup(manager => manager.UpdateAsync(seller)).ReturnsAsync(IdentityResult.Success);

        await CreateService(managers).TouchLastLoginAsync(42, UtcNow);

        seller.LastLoginAtUtc.Should().Be(UtcNow);
        seller.UpdatedAtUtc.Should().Be(UtcNow);
        managers.UserManager.Verify(manager => manager.UpdateAsync(seller), Times.Once);
    }

    [TestMethod]
    public async Task PublicOperation_WhenCancellationRequested_ThrowsBeforeCallingIdentity()
    {
        var managers = new IdentityManagerMocks();
        using var source = new CancellationTokenSource();
        source.Cancel();

        var act = () => CreateService(managers).EmailExistsAsync("seller@example.com", source.Token);

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
        managers.UserManager.Setup(manager => manager.CreateAsync(It.IsAny<Seller>(), "Password1"))
            .ReturnsAsync(IdentityResult.Success);
        return managers;
    }

    private static Seller CreateSeller() => new()
    {
        Id = 42,
        UserName = "seller@example.com",
        Email = "seller@example.com",
        FullName = "Seller Name",
        AccountStatus = "active"
    };

    private static IdentityResult Failed(string description) => IdentityResult.Failed(
        new IdentityError { Code = "unit_test", Description = description });
}
