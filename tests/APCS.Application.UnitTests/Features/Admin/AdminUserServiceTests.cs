using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin;
using APCS.Application.Features.Admin.Dtos.Request;
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
        Mock<IRepository<AuthToken>>? authTokenRepository = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        TimeProvider? timeProvider = null)
    {
        userRepository ??= new Mock<IRepository<User>>();
        planRepository ??= new Mock<IRepository<SubscriptionPlan>>();
        roleRepository ??= new Mock<IRepository<Role>>();
        authTokenRepository ??= new Mock<IRepository<AuthToken>>();
        unitOfWork ??= new Mock<IUnitOfWork>();
        timeProvider ??= TimeProvider.System;

        return new AdminUserService(
            userRepository.Object,
            planRepository.Object,
            roleRepository.Object,
            authTokenRepository.Object,
            unitOfWork.Object,
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
}
