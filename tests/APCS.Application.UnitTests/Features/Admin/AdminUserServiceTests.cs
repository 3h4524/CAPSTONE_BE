using System.Linq.Expressions;
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
        Mock<IRepository<UserRole>>? userRoleRepository = null,
        Mock<IRepository<AuthToken>>? authTokenRepository = null,
        Mock<IRepository<AuditLog>>? auditLogRepository = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<APCS.Application.Abstractions.Authentication.ICurrentUser>? currentUser = null,
        Mock<APCS.Application.Features.Auth.IAuthService>? authService = null,
        TimeProvider? timeProvider = null)
    {
        userRepository ??= new Mock<IRepository<User>>();
        planRepository ??= new Mock<IRepository<SubscriptionPlan>>();
        roleRepository ??= new Mock<IRepository<Role>>();
        userRoleRepository ??= new Mock<IRepository<UserRole>>();
        authTokenRepository ??= new Mock<IRepository<AuthToken>>();
        auditLogRepository ??= new Mock<IRepository<AuditLog>>();
        unitOfWork ??= new Mock<IUnitOfWork>();
        currentUser ??= new Mock<APCS.Application.Abstractions.Authentication.ICurrentUser>();
        authService ??= new Mock<APCS.Application.Features.Auth.IAuthService>();
        timeProvider ??= TimeProvider.System;

        return new AdminUserService(
            userRepository.Object,
            planRepository.Object,
            roleRepository.Object,
            userRoleRepository.Object,
            authTokenRepository.Object,
            auditLogRepository.Object,
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
    public async Task UpdateUserAsync_ValidRequest_UpdatesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, FullName = "Old Name" };
        var request = new UpdateAdminUserDto("New Name", "test@test.com", null, null, "Active", Array.Empty<string>());

        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User> { user }.AsQueryable().BuildMock();
        userRepository.Setup(x => x.Query()).Returns(users);

        var roleRepository = new Mock<IRepository<Role>>();
        var roles = new List<Role>().AsQueryable().BuildMock();
        roleRepository.Setup(x => x.Query()).Returns(roles);

        var userRoleRepository = new Mock<IRepository<UserRole>>();
        var userRoles = new List<UserRole>().AsQueryable().BuildMock();
        userRoleRepository.Setup(x => x.Query()).Returns(userRoles);

        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(
            userRepository: userRepository, 
            roleRepository: roleRepository, 
            userRoleRepository: userRoleRepository, 
            unitOfWork: unitOfWork);

        // Act
        var result = await service.UpdateUserAsync(userId, request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FullName.Should().Be("New Name");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UpdateUserAsync_FutureBirthday_ReturnsValidationError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, FullName = "Old Name" };
        var futureDate = DateTime.UtcNow.AddDays(1);
        var request = new UpdateAdminUserDto("New Name", "test@test.com", futureDate, null, "Active", Array.Empty<string>());

        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User> { user }.AsQueryable().BuildMock();
        userRepository.Setup(x => x.Query()).Returns(users);

        var service = CreateService(userRepository: userRepository);

        // Act
        var result = await service.UpdateUserAsync(userId, request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("validation.failed");
        result.Error.Message.Should().Contain("Birthday must be in the past");
    }

    [TestMethod]
    public async Task SuspendUserAsync_ValidRequest_SuspendsUserAndAddsAuditLog()
    {
        // Arrange
        var userId = Guid.NewGuid();
        Guid? adminId = Guid.NewGuid();
        var user = new User { Id = userId, AccountStatus = "Active", AuditLogs = new List<AuditLog>() };
        var activeToken = new AuthToken { UserId = userId, TokenHash = "token1", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        var request = new SuspendUserRequestDto("Violation of terms", 7);

        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User> { user }.AsQueryable().BuildMock();
        userRepository.Setup(x => x.Query()).Returns(users);
        userRepository.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var tokenRepository = new Mock<IRepository<AuthToken>>();
        var tokenList = new List<AuthToken> { activeToken };
        tokenRepository.Setup(x => x.FindAsync(It.IsAny<Expression<Func<AuthToken, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenList);

        var currentUserService = new Mock<APCS.Application.Abstractions.Authentication.ICurrentUser>();
        currentUserService.Setup(x => x.UserId).Returns(adminId);

        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(
            userRepository: userRepository,
            authTokenRepository: tokenRepository,
            currentUser: currentUserService,
            unitOfWork: unitOfWork);

        // Act
        var result = await service.SuspendUserAsync(userId, request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.AccountStatus.Should().Be("suspended");
        user.SuspendedUntil.Should().NotBeNull();
        user.SuspendedUntil.Value.Date.Should().Be(DateTime.UtcNow.AddDays(7).Date);
        user.AuditLogs.Should().HaveCount(1);
        user.AuditLogs.First().ActionType.Should().Be("SuspendUser");
        user.AuditLogs.First().NewValue.Should().Contain("\"status\":\"suspended\"");

        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UnlockUserAsync_ValidRequest_UnlocksUserAndAddsAuditLog()
    {
        // Arrange
        var userId = Guid.NewGuid();
        Guid? adminId = Guid.NewGuid();
        var user = new User { Id = userId, AccountStatus = "suspended", SuspendedUntil = DateTime.UtcNow.AddDays(7), AuditLogs = new List<AuditLog>() };

        var userRepository = new Mock<IRepository<User>>();
        var users = new List<User> { user }.AsQueryable().BuildMock();
        userRepository.Setup(x => x.Query()).Returns(users);
        userRepository.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var currentUserService = new Mock<APCS.Application.Abstractions.Authentication.ICurrentUser>();
        currentUserService.Setup(x => x.UserId).Returns(adminId);

        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(
            userRepository: userRepository,
            currentUser: currentUserService,
            unitOfWork: unitOfWork);

        // Act
        var result = await service.UnlockUserAsync(userId, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.AccountStatus.Should().Be("active");
        user.SuspendedUntil.Should().BeNull();
        user.AuditLogs.Should().HaveCount(1);
        user.AuditLogs.First().ActionType.Should().Be("UnlockUser");
        user.AuditLogs.First().NewValue.Should().Contain("\"status\":\"active\"");

        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
