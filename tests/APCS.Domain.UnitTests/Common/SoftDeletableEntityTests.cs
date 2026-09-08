using APCS.Domain.Common;
using FluentAssertions;

namespace APCS.Domain.UnitTests.Common;

[TestClass]
public sealed class SoftDeletableEntityTests
{
    [TestMethod]
    public void Delete_WhenCalledTwice_PreservesFirstDeletionTime()
    {
        var entity = new TestEntity();
        var first = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);

        entity.Delete(first);
        entity.Delete(first.AddHours(1));

        entity.DeletedAtUtc.Should().Be(first);
    }

    [TestMethod]
    public void Restore_WhenDeleted_ClearsDeletionTime()
    {
        var entity = new TestEntity();
        entity.Delete(DateTimeOffset.UtcNow);

        entity.Restore();

        entity.DeletedAtUtc.Should().BeNull();
    }

    private sealed class TestEntity : SoftDeletableEntity;
}
