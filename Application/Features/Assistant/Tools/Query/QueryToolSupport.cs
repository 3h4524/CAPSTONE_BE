using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Tools.Query;

internal static class QueryToolSupport
{
    public static AssistantToolOutcome Failure(Error error) =>
        AssistantToolOutcome.Error(string.IsNullOrWhiteSpace(error.Code) ? "failed" : error.Code.ToLowerInvariant(), error.Message);

    public static bool TryParseId(string? value, out Guid id) => Guid.TryParse(value, out id);

    public static string Clip(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value ?? string.Empty : value[..maxLength];
}
