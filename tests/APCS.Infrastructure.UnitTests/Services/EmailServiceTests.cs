using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class EmailServiceTests
{
    private const string VerificationToken = "raw-verification-token";

    [TestMethod]
    public async Task SendAsync_WhenCalled_CompletesAndDoesNotLogBody()
    {
        var logger = new TestLogger<EmailService>();
        var service = CreateService(logger);

        await service.SendAsync(
            "seller@example.com",
            "Welcome",
            "sensitive-body",
            CancellationToken.None);

        logger.Messages.Should().ContainSingle()
            .Which.Should().Contain("seller@example.com").And.Contain("Welcome");
        logger.Messages.Should().NotContain(message => message.Contains("sensitive-body"));
    }

    [TestMethod]
    public async Task SendEmailVerificationAsync_WhenCalled_DoesNotLogTheTokenAboveDebug()
    {
        var logger = new TestLogger<EmailService>(LogLevel.Information);
        var service = CreateService(logger);

        await service.SendEmailVerificationAsync(
            "seller@example.com",
            "Seller Name",
            VerificationToken,
            CancellationToken.None);

        logger.Messages.Should().NotContain(message => message.Contains(VerificationToken));
    }

    [TestMethod]
    public async Task SendEmailVerificationAsync_WhenDebugIsEnabled_LogsTheConfiguredClientLink()
    {
        var logger = new TestLogger<EmailService>();
        var service = CreateService(logger);

        await service.SendEmailVerificationAsync(
            "seller@example.com",
            "Seller Name",
            VerificationToken,
            CancellationToken.None);

        logger.Messages.Should().Contain(message =>
            message.Contains($"http://localhost:3000/verify-email?token={VerificationToken}"));
    }

    [TestMethod]
    public void SmtpOptions_WithoutAHost_IsNotConsideredConfigured()
    {
        new SmtpOptions().IsConfigured.Should().BeFalse();
        new SmtpOptions
        {
            Host = "smtp.gmail.com",
            FromAddress = "apcs@example.com"
        }.IsConfigured.Should().BeFalse("SMTP credentials are also required");
        new SmtpOptions
        {
            Host = "smtp.gmail.com",
            Username = "apcs@example.com",
            Password = "app-password",
            FromAddress = "apcs@example.com"
        }
            .IsConfigured.Should().BeTrue();
    }

    [TestMethod]
    public void SmtpOptions_UsesImplicitTls_OnlyOnPort465()
    {
        new SmtpOptions { Port = 465 }.UsesImplicitTls.Should().BeTrue();
        new SmtpOptions { Port = 587 }.UsesImplicitTls.Should().BeFalse();
    }

    /// <summary>
    /// Builds the service with no mail server configured, which is what local development and
    /// these tests run against: the message is logged instead of being sent over the network.
    /// </summary>
    private static EmailService CreateService(ILogger<EmailService> logger) => new(
        Microsoft.Extensions.Options.Options.Create(new AppOptions
        {
            BaseUrl = "http://localhost:3000",
            VerifyEmailPath = "/verify-email"
        }),
        Microsoft.Extensions.Options.Options.Create(new SmtpOptions()),
        logger);

    private sealed class TestLogger<T>(LogLevel minimumLevel = LogLevel.Trace) : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
