using APCS.Application.Features.Assistant.Common;

namespace APCS.Application.Features.Assistant.Tools;

public sealed record ChoiceArgs(string? Label, string? Value);

public sealed record OfferChoicesArgs(string? Question, IReadOnlyList<ChoiceArgs>? Options, bool? AllowFreeText);

public sealed class OfferChoicesTool : AssistantTool<OfferChoicesArgs>
{
    private const int MinOptions = 2;
    private const int MaxOptions = 5;
    private const int MaxQuestionLength = 200;
    private const int MaxLabelLength = 40;
    private const int MaxValueLength = 200;

    public override string Name => "offer_choices";

    public override AssistantToolKind Kind => AssistantToolKind.Interaction;

    protected override string Description =>
        "Show 2 to 5 short reply options as clickable chips when you need the user to choose or clarify. Do not use it to confirm write actions.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"question":{"type":"string","description":"Optional question shown above the options."},"options":{"type":"array","description":"2 to 5 options.","items":{"type":"object","properties":{"label":{"type":"string","description":"Chip text, at most 40 characters."},"value":{"type":"string","description":"Message sent when the chip is clicked."}},"required":["label","value"]}},"allowFreeText":{"type":"boolean"}},"required":["question","options"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) => true;

    protected override Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, OfferChoicesArgs arguments, CancellationToken cancellationToken)
    {
        var options = arguments.Options ?? [];
        var invalid = options.Count is < MinOptions or > MaxOptions
            || options.Any(option => string.IsNullOrWhiteSpace(option.Label) || option.Label.Length > MaxLabelLength
                || string.IsNullOrWhiteSpace(option.Value) || option.Value.Length > MaxValueLength)
            || arguments.Question is { Length: > MaxQuestionLength };
        if (invalid)
            return Task.FromResult(AssistantToolOutcome.Error("invalid_arguments", $"Provide {MinOptions} to {MaxOptions} options with short labels."));

        var choices = new AssistantChoicesData(
            arguments.Question,
            options.Select(option => new AssistantChoiceOptionData(option.Label!, option.Value!)).ToArray(),
            arguments.AllowFreeText ?? true);
        return Task.FromResult(AssistantToolOutcome.Ok(new { status = "shown" }, true, AssistantStreamEvent.Choices(choices)).WithFinalText(string.IsNullOrWhiteSpace(arguments.Question) ? null : string.Empty));
    }
}
