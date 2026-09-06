using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class EmailServiceTests
{
    [TestMethod]
    public async Task SendAsync_WhenCalled_CompletesAndDoesNotLogBody()
    {
        var logger = new TestLogger<EmailService>();
        var service = new EmailService(logger);

        await service.SendAsync(
            "seller@example.com",
            "Welcome",
            "sensitive-body",
            CancellationToken.None);

        logger.Messages.Should().ContainSingle()
            .Which.Should().Contain("seller@example.com").And.Contain("Welcome");
        logger.Messages.Should().NotContain(message => message.Contains("sensitive-body"));
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
