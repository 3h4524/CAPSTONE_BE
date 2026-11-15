namespace APCS.Application.Abstractions.Ai;

/// <summary>
/// Gemini refused the request or its answer under its own safety filter (<c>promptFeedback.blockReason</c>, or a
/// candidate finished for a safety reason), so it generated nothing. It derives from
/// <see cref="InvalidOperationException"/>, which callers already treat as an unusable answer.
/// </summary>
public sealed class GeminiContentBlockedException(string reason)
    : InvalidOperationException($"Gemini blocked the request: {reason}.")
{
    public string Reason { get; } = reason;
}
