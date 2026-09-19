using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin;
using APCS.Application.Features.Admin.Dtos.Request;
using APCS.Application.Features.Auth;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class AdminUserServiceTests
{
    private static AdminUserService CreateService(
        Mock<IRepository<User>>? userRepository = null,
        Mock<IRepository<SubscriptionPlan>>? planRepository = null,
        Mock<IRepository<Role>>? roleRepository = null,
        Mock<IRepository<UserRole>>? userRoleRepository = null,
        Mock<IRepository<AuthToken>>? authTokenRepository = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<ICurrentUser>? currentUser = null,
        Mock<IAuthService>? authService = null,
        TimeProvider? timeProvider = null)
    {
        userRepository ??= new Mock<IRepository<User>>();
        planRepository ??= new Mock<IRepository<SubscriptionPlan>>();
        roleRepository ??= new Mock<IRepository<Role>>();
        userRoleRepository ??= new Mock<IRepository<UserRole>>();
        authTokenRepository ??= new Mock<IRepository<AuthToken>>();
        unitOfWork ??= new Mock<IUnitOfWork>();
        currentUser ??= new Mock<ICurrentUser>();
        authService ??= new Mock<IAuthService>();
        timeProvider ??= TimeProvider.System;

        return new AdminUserService(
            userRepository.Object,
            planRepository.Object,
            roleRepository.Object,
            userRoleRepository.Object,
            authTokenRepository.Object,
            unitOfWork.Object,
            currentUser.Object,
            authService.Object,
            timeProvider,
            NullLogger<AdminUserService>.Instance);
    }

    [TestMethod]
    public async Task GetAvailablePlanNamesAsync_ShouldReturnDistinctPlanNames()
    {
        // Arrange
        var planRepository = new Mock<IRepository<SubscriptionPlan>>();
        var plans = new List<SubscriptionPlan>
        {
            new() { Name = "Free" },
            new() { Name = "Starter" },
            new() { Name = "Starter" },
            new() { Name = "Creator" },
            new() { Name = "Studio" },
            new() { Name = "Free" }
        }.AsQueryable().BuildMock();

        planRepository.Setup(x => x.Query()).Returns(plans);
        var service = CreateService(planRepository: planRepository);

        // Act
        var result = await service.GetAvailablePlanNamesAsync(CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Should().HaveCount(4);
        result.Value.Should().Contain(new[] { "Free", "Starter", "Creator", "Studio" });
    }

    [TestMethod]
    public async Task GetUsersAsync_NoFilters_ReturnsAllUsers()
    {
        // Arrange
        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User>
        {
            new() { Id = Guid.NewGuid(), FullName = "User 1", Email = "user1@test.com", AccountStatus = "Active", CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), FullName = "User 2", Email = "user2@test.com", AccountStatus = "Suspended", CreatedAt = DateTime.UtcNow }
        }.AsQueryable().BuildMock();

        userRepository.Setup(x => x.Query()).Returns(users);
        var service = CreateService(userRepository: userRepository);

        // Act
        var request = new GetUsersRequestDto { PageIndex = 1, PageSize = 10 };
        var result = await service.GetUsersAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
    }

    [TestMethod]
    public async Task GetUsersAsync_SearchTerm_ReturnsMatchedUsers()
    {
        // Arrange
        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User>
        {
            new() { Id = Guid.NewGuid(), FullName = "Alice", Email = "alice@test.com", AccountStatus = "Active", CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), FullName = "Bob", Email = "bob@test.com", AccountStatus = "Suspended", CreatedAt = DateTime.UtcNow }
        }.AsQueryable().BuildMock();

        userRepository.Setup(x => x.Query()).Returns(users);
        var service = CreateService(userRepository: userRepository);

        // Act
        var request = new GetUsersRequestDto { SearchTerm = "Alice", PageIndex = 1, PageSize = 10 };
        var result = await service.GetUsersAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items.First().FullName.Should().Be("Alice");
    }



    [TestMethod]
    public async Task GetUserByIdAsync_ExistingUser_ReturnsUserDto()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            FullName = "Test User",
            Email = "test@test.com",
            AccountStatus = "Active",
            CreatedAt = DateTime.UtcNow
        };

        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User> { user }.AsQueryable().BuildMock();
        userRepository.Setup(x => x.Query()).Returns(users);

        var service = CreateService(userRepository: userRepository);

        // Act
        var result = await service.GetUserByIdAsync(userId, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(userId);
        result.Value.FullName.Should().Be("Test User");
    }

    [TestMethod]
    public async Task GetUserByIdAsync_UserNotFound_ReturnsError()
    {
        // Arrange
        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User>().AsQueryable().BuildMock();
        userRepository.Setup(x => x.Query()).Returns(users);

        var service = CreateService(userRepository: userRepository);

        // Act
        var result = await service.GetUserByIdAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.NotFound");
    }

    [TestMethod]
    public async Task UpdateUserAsync_UserNotFound_ReturnsError()
    {
        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User>().AsQueryable().BuildMock();
        userRepository.Setup(x => x.Query()).Returns(users);

        var service = CreateService(userRepository: userRepository);
        var request = new UpdateAdminUserDto("Name", "test@test.com", null, null, "Active", new List<string>());
        var result = await service.UpdateUserAsync(Guid.NewGuid(), request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.NotFound");
    }

    [TestMethod]
    public async Task UpdateUserAsync_AdminCannotRemoveOwnRole_ReturnsError()
    {
        var adminId = Guid.NewGuid();
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        var user = new User
        {
            Id = adminId,
            Email = "admin@test.com",
            AccountStatus = "Active",
            UserRoleUsers = new List<UserRole> { new() { Role = role } }
        };

        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User> { user }.AsQueryable().BuildMock();
        userRepository.Setup(x => x.Query()).Returns(users);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.UserId).Returns(adminId);

        var service = CreateService(userRepository: userRepository, currentUser: currentUser);
        
        // Attempt to change roles to just "Seller" (removing "Admin")
        var request = new UpdateAdminUserDto("Admin User", "admin@test.com", null, null, "Active", new List<string> { "Seller" });
        var result = await service.UpdateUserAsync(adminId, request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Be("You cannot remove your own Admin role.");
    }

    [TestMethod]
    public async Task UpdateUserAsync_AdminCannotChangeOwnStatus_ReturnsError()
    {
        var adminId = Guid.NewGuid();
        var user = new User
        {
            Id = adminId,
            Email = "admin@test.com",
            AccountStatus = "Active"
        };

        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User> { user }.AsQueryable().BuildMock();
        userRepository.Setup(x => x.Query()).Returns(users);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.UserId).Returns(adminId);

        var service = CreateService(userRepository: userRepository, currentUser: currentUser);
        
        // Attempt to change status to "Suspended"
        var request = new UpdateAdminUserDto("Admin User", "admin@test.com", null, null, "Suspended", new List<string>());
        var result = await service.UpdateUserAsync(adminId, request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Be("You cannot change your own account status.");
    }
}
