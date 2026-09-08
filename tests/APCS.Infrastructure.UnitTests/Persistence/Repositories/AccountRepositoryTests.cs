using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.UnitTests.Persistence.Repositories;

[TestClass]
public sealed class AccountRepositoryTests
{
    [TestMethod]
    public async Task EmailExistsAsync_WhenEmailExists_ReturnsTrue()
    {
        using var context = CreateContext();
        var repository = new AccountRepository(context);
        await repository.AddAsync(CreateUser("user@example.com"), saveChange: true);

        var exists = await repository.EmailExistsAsync("user@example.com");

        exists.Should().BeTrue();
    }

    [TestMethod]
    public async Task EmailExistsAsync_WhenEmailDoesNotExist_ReturnsFalse()
    {
        using var context = CreateContext();
        var repository = new AccountRepository(context);

        var exists = await repository.EmailExistsAsync("missing@example.com");

        exists.Should().BeFalse();
    }

    [TestMethod]
    public async Task FindRoleByCodeAsync_WhenRoleDoesNotExist_ReturnsNull()
    {
        using var context = CreateContext();
        var repository = new AccountRepository(context);

        var role = await repository.FindRoleByCodeAsync(AuthConstants.UserRole);

        role.Should().BeNull();
    }

    [TestMethod]
    public async Task AddRole_ThenFindRoleByCodeAsync_ReturnsTheAddedRole()
    {
        using var context = CreateContext();
        var repository = new AccountRepository(context);
        var role = CreateRole(AuthConstants.UserRole);

        repository.AddRole(role);
        await context.SaveChangesAsync();

        var found = await repository.FindRoleByCodeAsync(AuthConstants.UserRole);
        found.Should().NotBeNull();
        found!.Id.Should().Be(role.Id);
    }

    [TestMethod]
    public async Task FindByEmailAsync_WhenSeveralUsersShareAnEmail_ReturnsTheOldest()
    {
        using var context = CreateContext();
        var repository = new AccountRepository(context);
        var oldest = CreateUser("user@example.com", createdAt: DateTime.UtcNow.AddDays(-2));
        var newest = CreateUser("user@example.com", createdAt: DateTime.UtcNow.AddDays(-1));
        await repository.AddAsync(newest, saveChange: true);
        await repository.AddAsync(oldest, saveChange: true);

        var found = await repository.FindByEmailAsync("user@example.com");

        found.Should().NotBeNull();
        found!.Id.Should().Be(oldest.Id);
    }

    [TestMethod]
    public async Task FindByEmailAsync_WhenNoUserMatches_ReturnsNull()
    {
        using var context = CreateContext();
        var repository = new AccountRepository(context);

        var found = await repository.FindByEmailAsync("missing@example.com");

        found.Should().BeNull();
    }

    [TestMethod]
    public async Task GetActiveRoleCodesAsync_ReturnsOnlyUnrevokedGrants()
    {
        using var context = CreateContext();
        var repository = new AccountRepository(context);
        var user = CreateUser("user@example.com");
        var activeRole = CreateRole("active-role");
        var revokedRole = CreateRole("revoked-role");
        await repository.AddAsync(user, saveChange: true);
        context.Roles.Add(activeRole);
        context.Roles.Add(revokedRole);
        await context.SaveChangesAsync();

        context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = activeRole.Id, GrantedAt = DateTime.UtcNow });
        context.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = revokedRole.Id,
            GrantedAt = DateTime.UtcNow,
            RevokedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var roleCodes = await repository.GetActiveRoleCodesAsync(user.Id);

        roleCodes.Should().ContainSingle().Which.Should().Be("active-role");
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static User CreateUser(string email, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        FullName = "Test User",
        AccountStatus = AccountStatuses.PendingVerification,
        EmailVerified = false,
        CreatedAt = createdAt ?? DateTime.UtcNow,
        UpdatedAt = createdAt ?? DateTime.UtcNow
    };

    private static Role CreateRole(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        IsSystemRole = true,
        CreatedAt = DateTime.UtcNow
    };
}
