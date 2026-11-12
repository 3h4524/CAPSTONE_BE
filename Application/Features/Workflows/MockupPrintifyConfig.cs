namespace APCS.Application.Features.Workflows;

/// <summary>
/// The Printify product each product type is made as, set on the Apply mock-up node. The blueprint comes from the
/// mock-up templates chosen there (a system template linked to a Printify blueprint shows exactly that garment), so the
/// listing's mock-ups and the item Printify ships are the same; the colors are the template colors mapped to the
/// provider's own color names. Push to Printify only shows these and creates the product.
/// </summary>
/// <param name="PrintifyProducts">The Printify product per APCS product type, e.g. <c>tshirt</c>.</param>
public sealed record MockupPrintifyConfig(IReadOnlyDictionary<string, PrintifyProductSetup>? PrintifyProducts)
{
    public bool HasProducts => PrintifyProducts is { Count: > 0 };

    /// <summary>The Printify product chosen for <paramref name="productType"/>, or null when there is none.</summary>
    public PrintifyProductSetup? SetupFor(string? productType) =>
        productType is null || PrintifyProducts is null ? null
        : PrintifyProducts.FirstOrDefault(pair => string.Equals(pair.Key, productType, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>The Apply mock-up node's Printify settings in a workflow definition, or null without that node.</summary>
    public static MockupPrintifyConfig? From(WorkflowDefinition definition) =>
        definition.Nodes.FirstOrDefault(node => node.Type == "apply-mockup") is { } node ? WorkflowJson.Config<MockupPrintifyConfig>(node) : null;
}

/// <summary>One Printify product: a blueprint made by one provider in some colors and sizes, printed in one area.</summary>
/// <param name="Colors">Printify's own color names, mapped from the template colors chosen for the mock-ups.</param>
/// <param name="Position">The print area, <c>front</c> unless chosen.</param>
public sealed record PrintifyProductSetup(
    long? BlueprintId,
    string? BlueprintTitle,
    long? PrintProviderId,
    string? PrintProviderTitle,
    IReadOnlyList<string>? Colors,
    IReadOnlyList<string>? Sizes,
    string? Position = null);
