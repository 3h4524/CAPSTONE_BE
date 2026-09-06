using APCS.Application.Common.Behaviours;
using APCS.Application.UnitTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace APCS.Application.UnitTests.Common.Behaviours;

[TestClass]
public sealed class LoggingBehaviourTests
{
    [TestMethod]
    public async Task Handle_WhenSuccessful_LogsStartAndCompletionWithoutPayload()
    {
        var logger = new ListLogger<LoggingBehaviour<SensitiveRequest, string>>();
        var behaviour = new LoggingBehaviour<SensitiveRequest, string>(logger);

        var result = await behaviour.Handle(
            new SensitiveRequest("do-not-log"),
            () => Task.FromResult("done"),
            CancellationToken.None);

        result.Should().Be("done");
        logger.Entries.Should().HaveCount(2);
        logger.Entries.Should().OnlyContain(entry => entry.Level == LogLevel.Information);
        logger.Entries.Should().OnlyContain(entry => entry.Message.Contains(nameof(SensitiveRequest)));
        logger.Entries.Should().NotContain(entry => entry.Message.Contains("do-not-log"));
    }

    [TestMethod]
    public async Task Handle_WhenNextThrows_LogsAndRethrowsSameException()
    {
        var logger = new ListLogger<LoggingBehaviour<SensitiveRequest, string>>();
        var behaviour = new LoggingBehaviour<SensitiveRequest, string>(logger);
        var expected = new InvalidOperationException("boom");

        var act = () => behaviour.Handle(
            new SensitiveRequest("secret"),
            () => Task.FromException<string>(expected),
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expected);
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error && entry.Exception == expected);
    }

    private sealed record SensitiveRequest(string Secret);
}
