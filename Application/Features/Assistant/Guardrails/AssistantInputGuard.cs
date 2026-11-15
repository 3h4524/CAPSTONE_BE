using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace APCS.Application.Features.Assistant.Guardrails;

public enum AssistantInputVerdictKind
{
    Allow,
    Flag,
    Block
}

public sealed record AssistantInputVerdict(AssistantInputVerdictKind Kind, string? Rule = null)
{
    public static readonly AssistantInputVerdict Allowed = new(AssistantInputVerdictKind.Allow);
}

public sealed partial class AssistantInputGuard
{
    public const string VietnameseRefusal =
        "Mình không thể làm theo yêu cầu đó. Mình có thể giúp bạn với workflow, batch, sản phẩm, lượt chạy và các câu hỏi về Podivo.";

    public const string EnglishRefusal =
        "I can't help with that request. I can help with your workflows, batches, products, runs and questions about Podivo.";

    private static readonly (string Rule, Regex Pattern)[] BlockRules =
    [
        ("reveal_instructions", RevealInstructions()),
        ("ignore_instructions", IgnoreInstructions()),
        ("jailbreak_persona", JailbreakPersona()),
        ("reveal_secrets", RevealSecrets())
    ];

    private static readonly (string Rule, Regex Pattern)[] FlagRules =
    [
        ("role_override", RoleOverride()),
        ("fake_role_marker", FakeRoleMarker())
    ];

    public AssistantInputVerdict Check(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return AssistantInputVerdict.Allowed;

        var text = Normalize(message);
        foreach (var (rule, pattern) in BlockRules)
        {
            if (pattern.IsMatch(text))
                return new AssistantInputVerdict(AssistantInputVerdictKind.Block, rule);
        }

        foreach (var (rule, pattern) in FlagRules)
        {
            if (pattern.IsMatch(text))
                return new AssistantInputVerdict(AssistantInputVerdictKind.Flag, rule);
        }

        return AssistantInputVerdict.Allowed;
    }

    public static string RefusalFor(string message) =>
        VietnameseLetters().IsMatch(message) ? VietnameseRefusal : EnglishRefusal;

    private static string Normalize(string message)
    {
        var decomposed = message.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return WhiteSpace().Replace(builder.ToString(), " ");
    }

    [GeneratedRegex(@"\b(show|reveal|print|display|repeat|output|dump|leak|tell me|give me|share)\b.{0,40}\b(system prompt|system message|initial prompt|hidden (prompt|instructions|rules)|(your|the) (instructions|prompt|system message))\b|\b(reveal|print|repeat|dump|leak)\b.{0,20}\byour (rules|guidelines)\b|\bwhat (is|are|were) (your|the) (system prompt|initial prompt|hidden instructions|instructions you (were|are) given)\b|\b(cho (toi |minh )?(xem|biet)|in ra|tiet lo|nhac lai|dua (toi|minh|ra)|chia se|hien thi|liet ke)\b.{0,40}\b(system prompt|prompt he thong|chi dan (he thong|an|goc)|huong dan (he thong|an|goc)|luat (an|goc)|quy tac (an|goc)|cau lenh goc|prompt (goc|cua ban))\b")]
    private static partial Regex RevealInstructions();

    [GeneratedRegex(@"\b(ignore|disregard|forget|override|bypass)\b.{0,20}\b(all|any|your|previous|prior|above|earlier)\b.{0,20}\b(instructions|rules|prompts?|guidelines|restrictions)\b|\b(bo qua|phot lo|quen het|quen di)\b.{0,25}\b(huong dan|chi dan|luat|quy tac)\b.{0,15}\b(truoc|tren|cua ban|he thong|da cho|goc|ban dau)\b")]
    private static partial Regex IgnoreInstructions();

    [GeneratedRegex(@"\bdan mode\b|\byou are dan\b|\bdo anything now\b|\bdeveloper mode\b|\bjailbreak\b|\bgod mode\b|\bche do (nha phat trien|developer|khong gioi han)\b|\bunrestricted mode\b")]
    private static partial Regex JailbreakPersona();

    [GeneratedRegex(@"\b(show|give|print|reveal|tell|share|send|what is)\b.{0,30}\b(your|system|server|backend|gemini|podivo|internal)\b.{0,20}\b(api ?keys?|secret ?keys?|signing keys?|connection strings?|database passwords?|jwt secrets?|access tokens?)\b|\b(cho|dua|gui|tiet lo|in ra)\b.{0,30}\b(api ?key|khoa api|secret key|connection string|mat khau|signing key|token)\b.{0,20}\b(he thong|cua ban|server|backend|gemini|podivo|noi bo|podi)\b")]
    private static partial Regex RevealSecrets();

    [GeneratedRegex(@"\b(you are now|from now on you are|pretend (to be|you are)|act as (a|an|my)?\s*(unrestricted|unfiltered|different|new|evil)|roleplay as|tu gio ban la|gia vo (la|lam)|dong vai(?! tro))\b")]
    private static partial Regex RoleOverride();

    [GeneratedRegex(@"(^|\n)\s*(system|assistant)\s*:|<\s*/?\s*(system|instructions?|app_context|security_note)\s*>|\[/?inst\]|###\s*(instruction|system)")]
    private static partial Regex FakeRoleMarker();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex("[ăâđêôơưĂÂĐÊÔƠƯàáảãạằắẳẵặầấẩẫậèéẻẽẹềếểễệìíỉĩịòóỏõọồốổỗộờớởỡợùúủũụừứửữựỳýỷỹỵ]")]
    private static partial Regex VietnameseLetters();
}
