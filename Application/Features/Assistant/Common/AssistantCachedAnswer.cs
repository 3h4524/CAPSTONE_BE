namespace APCS.Application.Features.Assistant.Common;

public sealed record AssistantCachedAnswer(string Text, IReadOnlyList<AssistantCitationData> Citations);
