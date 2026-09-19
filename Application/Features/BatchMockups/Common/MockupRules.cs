namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>Shared limits for batch mock-up selection.</summary>
public static class MockupRules
{
    public const int MinimumSelection = 1;
    public const int MaximumSelection = 5;
    public const string ConfigKey = "mockupTemplateIds";

    public static readonly IReadOnlyList<string> ValidProductTypes =
        ["tshirt", "hoodie", "mug", "poster", "tote_bag", "phone_case"];
}
