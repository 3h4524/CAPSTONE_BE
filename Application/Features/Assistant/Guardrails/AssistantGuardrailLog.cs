using APCS.Application.Features.Assistant.Common;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Assistant.Guardrails;

public static class AssistantGuardrailKinds
{
    public const string InputBlock = "input_block";
    public const string InputFlag = "input_flag";
    public const string SecretRedacted = "secret_redacted";
    public const string PromptLeak = "prompt_leak";
    public const string ToolNameMention = "tool_name_mention";
    public const string ModelBlocked = "model_blocked";
}

public static class AssistantGuardrailLog
{
    private const int ExcerptChars = 60;

    public static void Triggered(ILogger logger, string kind, string rule, AssistantChatSession session, string? excerpt = null) =>
        logger.LogWarning(
            "Assistant guardrail {GuardrailKind} ({GuardrailRule}) for user {UserId} in conversation {ConversationId}: {Excerpt}",
            kind,
            rule,
            session.UserId,
            session.ConversationId,
            Excerpt(excerpt));

    private static string Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var singleLine = text.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= ExcerptChars ? singleLine : singleLine[..ExcerptChars] + "…";
    }
}
