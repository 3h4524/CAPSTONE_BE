using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Application.Features.ListingStreams;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Application.UnitTests.Features.ListingStreams;

[TestClass]
public sealed class ListingStreamPublisherTests
{
    private readonly FakeListingStreamBuffer buffer = new();
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid streamId = Guid.NewGuid();
    private readonly Guid productId = Guid.NewGuid();

    private ListingStreamPublisher Publisher(Guid? user)
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.UserId).Returns(user);
        return new(buffer, current.Object, NullLogger<ListingStreamWriter>.Instance);
    }

    [TestMethod]
    public async Task Open_WritesEachEventAsCamelCaseJsonToTheCallersStream()
    {
        var writer = Publisher(userId).Open(streamId)!;
        var observer = writer.For(productId);
        var listing = new ListingContentResponseDto(productId, Guid.NewGuid(), "Road trip tee", "A soft tee.", [new("tag", "primary", 1)], 1, "text-model", null);

        await observer.StartedAsync("text-model", default);
        await observer.TextAsync("{\"ti", default);
        await observer.RestartedAsync(default);
        await observer.CompletedAsync(listing, default);
        await observer.FailedAsync("Gemini is busy.", default);
        await writer.EndAsync();

        var entries = buffer.Entries(userId, streamId);
        entries.Select(entry => entry.Type).Should().Equal("start", "delta", "reset", "done", "failed", "end");
        entries[0].Data.Should().Be($$"""{"productId":"{{productId}}","model":"text-model"}""");
        JsonSerializer.Deserialize<JsonElement>(entries[1].Data).GetProperty("text").GetString().Should().Be("{\"ti");
        entries[2].Data.Should().Be($$"""{"productId":"{{productId}}"}""");
        entries[3].Data.Should().StartWith($$"""{"productId":"{{productId}}","listing":{"productId":"{{productId}}",""")
            .And.Contain("\"title\":\"Road trip tee\"").And.Contain("\"tags\":[{\"value\":\"tag\",\"type\":\"primary\",\"position\":1}]");
        entries[4].Data.Should().Be($$"""{"productId":"{{productId}}","message":"Gemini is busy."}""");
        entries[5].Data.Should().Be("{}");
    }

    [TestMethod]
    public async Task EndAsync_CalledTwice_WritesEndOnce()
    {
        var writer = Publisher(userId).Open(streamId)!;

        await writer.EndAsync();
        await writer.EndAsync();

        buffer.Entries(userId, streamId).Should().ContainSingle().Which.Type.Should().Be("end");
    }

    [TestMethod]
    public async Task Observer_WhenRedisFails_LogsAndCarriesOn()
    {
        var writer = Publisher(userId).Open(streamId)!;
        buffer.AppendFailure = new InvalidOperationException("Redis is down.");

        var write = async () =>
        {
            await writer.For(productId).TextAsync("chunk", default);
            await writer.EndAsync();
        };

        await write.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Observer_WhenTheRequestIsCancelled_StillThrows()
    {
        var writer = Publisher(userId).Open(streamId)!;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        buffer.AppendFailure = new OperationCanceledException(cancelled.Token);

        var write = () => writer.For(productId).TextAsync("chunk", cancelled.Token);

        await write.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public void Open_WithoutAStreamIdOrASignedInUser_ReturnsNothing()
    {
        Publisher(userId).Open(null).Should().BeNull();
        Publisher(userId).Open(Guid.Empty).Should().BeNull();
        Publisher(null).Open(streamId).Should().BeNull();
    }
}
