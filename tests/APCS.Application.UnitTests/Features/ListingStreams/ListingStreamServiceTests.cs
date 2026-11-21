using APCS.Application.Abstractions.Authentication;
using APCS.Application.Features.ListingStreams;
using APCS.Common.Models;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.ListingStreams;

[TestClass]
public sealed class ListingStreamServiceTests
{
    private readonly FakeListingStreamBuffer buffer = new();
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid streamId = Guid.NewGuid();

    private ListingStreamService Service(Guid? user)
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.UserId).Returns(user);
        return new(current.Object, new ListingStreamReader(buffer, TimeProvider.System));
    }

    [TestMethod]
    public async Task Attach_ReadsTheCallersStreamAfterTheGivenId()
    {
        var seen = await buffer.AppendAsync(userId, streamId, "start", "{}");
        await buffer.AppendAsync(userId, streamId, "end", "{}");

        var result = Service(userId).Attach(streamId, $" {seen} ");

        result.IsSuccess.Should().BeTrue();
        var types = new List<string>();
        await foreach (var entry in result.Value)
            types.Add(entry.Type);
        types.Should().Equal("end");
    }

    [TestMethod]
    [DataRow("abc")]
    [DataRow("1-")]
    [DataRow("1-0-0")]
    public void Attach_WithAnInvalidEventId_IsAValidationError(string afterId)
    {
        var result = Service(userId).Attach(streamId, afterId);

        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [TestMethod]
    public void Attach_WithoutASignedInUser_IsUnauthorized()
    {
        var result = Service(null).Attach(streamId, null);

        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }
}
