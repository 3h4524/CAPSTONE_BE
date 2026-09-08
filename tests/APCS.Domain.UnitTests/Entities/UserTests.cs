using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Domain.UnitTests.Entities;

[TestClass]
public sealed class UserTests
{
    [TestMethod]
    [DataRow("active", true)]
    [DataRow("ACTIVE", true)]
    [DataRow("locked", false)]
    [DataRow("suspended", false)]
    [DataRow("pending_verification", false)]
    public void CanAuthenticate_ForAccountStatus_ReturnsExpectedResult(
        string accountStatus,
        bool expected)
    {
        var user = new User
        {
            AccountStatus = accountStatus
        };

        user.CanAuthenticate.Should().Be(expected);
    }

    [TestMethod]
    public void CanAuthenticate_WhenActiveAccountWasDeleted_ReturnsFalse()
    {
        var user = new User
        {
            AccountStatus = AccountStatuses.Active,
            DeletedAt = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc)
        };

        user.CanAuthenticate.Should().BeFalse();
    }
}
