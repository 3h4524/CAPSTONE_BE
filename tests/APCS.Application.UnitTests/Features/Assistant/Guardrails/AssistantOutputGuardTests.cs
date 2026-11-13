using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Guardrails;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Assistant.Guardrails;

[TestClass]
public sealed class AssistantOutputGuardTests
{
    private const string GoogleKey = "AIzaSyD4f8Kq2mXzPq7LwT9vB3nH6jR1sY0uE5c";

    private static AssistantOutputGuard Guard() => new(AssistantPromptBuilder.Instructions, ["get_run_progress", "run_workflow"]);

    private static string Stream(AssistantOutputGuard guard, params string[] chunks) =>
        string.Concat(chunks.Select(guard.Push)) + guard.Flush();

    [TestMethod]
    public void Push_PlainText_IsPassedOnRightAway()
    {
        var guard = Guard();

        guard.Push("Hello").Should().Be("Hello");
        guard.Push(" there, the run is ready.").Should().Be(" there, the run is ready.");
    }

    [TestMethod]
    public void Push_AKeySplitAcrossChunks_NeverReachesTheUser()
    {
        var guard = Guard();
        var pushed = new List<string> { guard.Push("Your key is AI"), guard.Push(GoogleKey[2..20]), guard.Push(GoogleKey[20..]), guard.Push(" done.") };
        pushed.Add(guard.Flush());

        string.Concat(pushed).Should().Be($"Your key is {AssistantOutputGuard.Mask} done.");
        pushed.Should().NotContain(chunk => chunk.Contains("AIza"));
        guard.RedactedSecretRules.Should().Contain("google_api_key");
    }

    [TestMethod]
    [DataRow("Connection: Host=db;Password=s3cr3tValue;Port=5432", "Password=" + AssistantOutputGuard.Mask)]
    [DataRow("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2lnbmF0dXJlLXZhbHVl ok", "token " + AssistantOutputGuard.Mask + " ok")]
    [DataRow("use sk-abcdefghijklmnopqrstuvwx now", "use " + AssistantOutputGuard.Mask + " now")]
    public void Stream_OtherSecrets_AreMasked(string text, string expectedFragment)
    {
        var guard = Guard();

        var output = Stream(guard, text[..10], text[10..]);

        output.Should().Contain(expectedFragment);
    }

    [TestMethod]
    public void Push_TheSystemPromptRepeated_StopsTheAnswer()
    {
        var guard = Guard();
        var prompt = AssistantPromptBuilder.Instructions;
        var start = prompt.IndexOf("Grounding:", StringComparison.Ordinal);

        var output = Stream(guard, "Sure, here it is: ", prompt.Substring(start, 400), prompt.Substring(start + 400, 400));

        guard.LeakDetected.Should().BeTrue();
        output.Should().NotContain("help articles are written in English");
        guard.Sanitize("anything").Should().BeEmpty();
    }

    [TestMethod]
    public void Push_AnAnswerThatQuotesOneShortPhrase_IsNotALeak()
    {
        var guard = Guard();

        Stream(guard, "I am Podi, the Podivo assistant. Your workflow has three steps and is ready to run.");

        guard.LeakDetected.Should().BeFalse();
    }

    [TestMethod]
    public void MentionedToolNames_FindsRawToolNamesInTheAnswer()
    {
        var guard = Guard();

        guard.MentionedToolNames("I will call get_run_progress now").Should().Equal("get_run_progress");
        guard.MentionedToolNames("I checked the run").Should().BeEmpty();
    }

    [TestMethod]
    public void Sanitize_MasksSecretsInTheSavedText() =>
        Guard().Sanitize($"key {GoogleKey}").Should().Be($"key {AssistantOutputGuard.Mask}");
}
