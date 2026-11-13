using System.Text;
using System.Text.RegularExpressions;

namespace APCS.Application.Features.Assistant.Guardrails;

public sealed partial class AssistantOutputGuard
{
    public const string Mask = "••••";
    private static readonly string[] SecretPrefixes = ["AIza", "sk-", "eyJ", "-----"];
    private const int ShingleWords = 8;
    private const int LeakShingles = 3;

    private readonly HashSet<string> promptShingles;
    private readonly IReadOnlyCollection<string> toolNames;
    private readonly StringBuilder pending = new();
    private readonly StringBuilder emitted = new();
    private readonly HashSet<string> secretRules = [];

    public AssistantOutputGuard(string systemPrompt, IReadOnlyCollection<string> toolNames)
    {
        promptShingles = Shingles(systemPrompt);
        this.toolNames = toolNames;
    }

    public bool LeakDetected { get; private set; }

    public IReadOnlyCollection<string> RedactedSecretRules => secretRules;

    public string Push(string delta)
    {
        if (LeakDetected)
            return string.Empty;

        pending.Append(delta);
        var redacted = Redact(pending.ToString());
        pending.Clear().Append(redacted);
        if (LeaksPrompt(emitted + redacted))
        {
            LeakDetected = true;
            pending.Clear();
            return string.Empty;
        }

        var cut = SafeCut(redacted);
        if (cut <= 0)
            return string.Empty;

        var ready = redacted[..cut];
        pending.Remove(0, cut);
        emitted.Append(ready);
        return ready;
    }

    public string Flush()
    {
        if (LeakDetected || pending.Length == 0)
            return string.Empty;

        var ready = Redact(pending.ToString());
        pending.Clear();
        if (LeaksPrompt(emitted + ready))
        {
            LeakDetected = true;
            return string.Empty;
        }

        emitted.Append(ready);
        return ready;
    }

    public string Sanitize(string text) => LeakDetected ? string.Empty : Redact(text);

    public IReadOnlyList<string> MentionedToolNames(string text) =>
        toolNames.Where(name => Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b")).ToArray();

    private string Redact(string text)
    {
        foreach (var (rule, pattern) in SecretRules)
        {
            text = pattern.Replace(text, match =>
            {
                secretRules.Add(rule);
                return match.Groups["keep"].Success ? match.Groups["keep"].Value + Mask : Mask;
            });
        }

        return text;
    }

    private static int SafeCut(string text)
    {
        var tokenStart = text.Length;
        while (tokenStart > 0 && IsTokenChar(text[tokenStart - 1]))
            tokenStart--;
        var tail = text[tokenStart..];
        if (tail.Length > 0 && SecretPrefixes.Any(prefix => prefix.StartsWith(tail, StringComparison.Ordinal) || tail.StartsWith(prefix, StringComparison.Ordinal)))
            return tokenStart;
        var password = PasswordTail().Match(text);
        return password.Success ? password.Index : text.Length;
    }

    private static bool IsTokenChar(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or '+' or '/' or '=';

    private bool LeaksPrompt(string text)
    {
        if (promptShingles.Count == 0)
            return false;
        var matches = 0;
        foreach (var shingle in Shingles(text))
        {
            if (promptShingles.Contains(shingle) && ++matches >= LeakShingles)
                return true;
        }

        return false;
    }

    private static HashSet<string> Shingles(string text)
    {
        var words = Words().Matches(text.ToLowerInvariant()).Select(match => match.Value).ToArray();
        var shingles = new HashSet<string>();
        for (var index = 0; index + ShingleWords <= words.Length; index++)
            shingles.Add(string.Join(' ', words, index, ShingleWords));
        return shingles;
    }

    private static readonly (string Rule, Regex Pattern)[] SecretRules =
    [
        ("google_api_key", GoogleApiKey()),
        ("openai_key", OpenAiKey()),
        ("jwt", Jwt()),
        ("password_assignment", PasswordAssignment()),
        ("private_key", PrivateKey())
    ];

    [GeneratedRegex(@"AIza[0-9A-Za-z_\-]{30,}")]
    private static partial Regex GoogleApiKey();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]{20,}")]
    private static partial Regex OpenAiKey();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?<keep>\b(password|pwd|passwd)\s*[=:]\s*)[^\s;,""']+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordAssignment();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(-----END [A-Z ]*PRIVATE KEY-----|$)")]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"\b(password|pwd|passwd)\s*[=:]?\s*\S*$", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordTail();

    [GeneratedRegex(@"[\p{L}\p{N}_]+")]
    private static partial Regex Words();
}
