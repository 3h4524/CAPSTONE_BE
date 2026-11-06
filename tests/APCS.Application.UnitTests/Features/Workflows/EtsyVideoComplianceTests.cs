using APCS.Application.Features.Workflows;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class EtsyVideoComplianceTests
{
    [TestMethod]
    public void Evaluate_WhenAllRequirementsAreMet_ReturnsPass()
    {
        var result = EtsyVideoCompliance.Evaluate(Qa(), "1:2", "mp4");

        result.Status.Should().Be("pass");
        result.Checks.Should().OnlyContain(check => check.Status == "pass");
    }

    [TestMethod]
    [DataRow("mp4")]
    [DataRow("mov")]
    [DataRow("flv")]
    [DataRow("aac")]
    [DataRow("avi")]
    [DataRow("3gp")]
    [DataRow("mpeg")]
    [DataRow(" MOV ")]
    public void Evaluate_SupportedFormat_IsAccepted(string format)
    {
        var result = EtsyVideoCompliance.Evaluate(Qa(), "2:1", format);

        result.Checks.Single(check => check.Key == "format").Status.Should().Be("pass");
        result.Checks.Single(check => check.Key == "aspectRatio").Status.Should().Be("pass");
    }

    [TestMethod]
    public void Evaluate_BelowRecommendedResolutionAndAspectRatio_ReturnsWarnings()
    {
        var result = EtsyVideoCompliance.Evaluate(Qa(width: 500, height: 800, hasAudio: true), "9:16", "mp4");

        result.Status.Should().Be("pass_with_warning");
        result.Checks.Single(check => check.Key == "resolution").Status.Should().Be("warning");
        result.Checks.Single(check => check.Key == "aspectRatio").Status.Should().Be("warning");
        result.Checks.Single(check => check.Key == "audio").Status.Should().Be("info");
    }

    [TestMethod]
    public void Evaluate_ResolutionBelowMinimum_ReturnsFail()
    {
        var result = EtsyVideoCompliance.Evaluate(Qa(width: 499, height: 800), "2:1", "mp4");

        result.Status.Should().Be("fail");
        result.Checks.Single(check => check.Key == "resolution").Status.Should().Be("fail");
    }

    [TestMethod]
    public void Evaluate_InvalidDecodeDurationAndFormat_ReturnsFail()
    {
        var qa = Qa(durationSeconds: 4, fullDecode: false);

        var result = EtsyVideoCompliance.Evaluate(qa, "4:3", "wmv");

        result.Status.Should().Be("fail");
        result.Checks.Single(check => check.Key == "decode").Status.Should().Be("fail");
        result.Checks.Single(check => check.Key == "duration").Status.Should().Be("fail");
        result.Checks.Single(check => check.Key == "format").Status.Should().Be("fail");
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(104_857_601L)]
    public void Evaluate_EmptyOrOversizedFile_ReturnsFail(long bytes)
    {
        var result = EtsyVideoCompliance.Evaluate(Qa(bytes: bytes), "1:2", "mp4");

        result.Status.Should().Be("fail");
        result.Checks.Single(check => check.Key == "fileSize").Status.Should().Be("fail");
    }

    private static MediaQa Qa(int width = 1080, int height = 2160, double durationSeconds = 5,
        long bytes = 1_000_000, bool hasAudio = false, bool fullDecode = true) =>
        new("h264", "yuv420p", width, height, 30, durationSeconds, bytes, hasAudio, fullDecode);
}
