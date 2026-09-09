using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.UnitTests.Persistence.Repositories;

[TestClass]
public sealed class RepositoryTests
{
    [TestMethod]
    public async Task AddAsync_ByDefault_StagesTheEntityWithoutSaving()
    {
        using var context = CreateContext();
        var repository = new Repository<AuthToken>(context);
        var token = CreateToken();

        await repository.AddAsync(token);

        context.ChangeTracker.Entries<AuthToken>().Should().ContainSingle(entry => entry.State == EntityState.Added);
    }

    [TestMethod]
    public async Task AddAsync_WithSaveChangeTrue_PersistsImmediately()
    {
        using var context = CreateContext();
        var repository = new Repository<AuthToken>(context);
        var token = CreateToken();

        await repository.AddAsync(token, saveChange: true);

        var found = await repository.GetByIdAsync(token.Id);
        found.Should().NotBeNull();
        found!.TokenHash.Should().Be(token.TokenHash);
    }

    [TestMethod]
    public async Task GetByIdAsync_WhenEntityDoesNotExist_ReturnsNull()
    {
        using var context = CreateContext();
        var repository = new Repository<AuthToken>(context);

        var found = await repository.GetByIdAsync(Guid.NewGuid());

        found.Should().BeNull();
    }

    [TestMethod]
    public async Task GetAllAsync_ReturnsEveryEntityInTheSet()
    {
        using var context = CreateContext();
        var repository = new Repository<AuthToken>(context);
        await repository.AddAsync(CreateToken(), saveChange: true);
        await repository.AddAsync(CreateToken(), saveChange: true);

        var all = await repository.GetAllAsync();

        all.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task FindAsync_ReturnsOnlyEntitiesMatchingThePredicate()
    {
        using var context = CreateContext();
        var repository = new Repository<AuthToken>(context);
        var matching = CreateToken(userId: Guid.NewGuid());
        await repository.AddAsync(matching, saveChange: true);
        await repository.AddAsync(CreateToken(), saveChange: true);

        var found = await repository.FindAsync(candidate => candidate.UserId == matching.UserId);

        found.Should().ContainSingle().Which.Id.Should().Be(matching.Id);
    }

    [TestMethod]
    public async Task Query_ReturnsUntrackedMatchingEntities()
    {
        using var context = CreateContext();
        var repository = new Repository<AuthToken>(context);
        var token = CreateToken();
        await repository.AddAsync(token, saveChange: true);

        var queried = repository.Query().Single(candidate => candidate.Id == token.Id);

        context.Entry(queried).State.Should().Be(EntityState.Detached);
        queried.TokenHash.Should().Be(token.TokenHash);
    }

    [TestMethod]
    public async Task UpdateAsync_WithSaveChangeTrue_PersistsChangesImmediately()
    {
        using var context = CreateContext();
        var repository = new Repository<AuthToken>(context);
        var token = CreateToken();
        await repository.AddAsync(token, saveChange: true);

        token.Revoke(DateTimeOffset.UtcNow);
        await repository.UpdateAsync(token, saveChange: true);

        var found = await repository.GetByIdAsync(token.Id);
        found!.IsRevoked.Should().BeTrue();
    }

    [TestMethod]
    public async Task RemoveAsync_WithSaveChangeTrue_DeletesTheEntityImmediately()
    {
        using var context = CreateContext();
        var repository = new Repository<AuthToken>(context);
        var token = CreateToken();
        await repository.AddAsync(token, saveChange: true);

        await repository.RemoveAsync(token, saveChange: true);

        var found = await repository.GetByIdAsync(token.Id);
        found.Should().BeNull();
    }

    [TestMethod]
    public async Task RemoveAsync_ByDefault_StagesTheDeletionWithoutSaving()
    {
        using var context = CreateContext();
        var repository = new Repository<AuthToken>(context);
        var token = CreateToken();
        await repository.AddAsync(token, saveChange: true);

        await repository.RemoveAsync(token);

        context.Entry(token).State.Should().Be(EntityState.Deleted);

        await context.SaveChangesAsync();
        (await repository.GetByIdAsync(token.Id)).Should().BeNull();
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static AuthToken CreateToken(Guid? userId = null) => AuthToken.CreateRefreshToken(
        userId ?? Guid.NewGuid(),
        Guid.NewGuid().ToString(),
        DateTimeOffset.UtcNow.AddDays(7),
        DateTimeOffset.UtcNow);
}
