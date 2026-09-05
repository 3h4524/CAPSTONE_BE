using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Selects a mockup template for a product, in a chosen order.
/// </summary>
public sealed class ProductMockupTemplate : CreationTrackedEntity
{
    private ProductMockupTemplate()
    {
    }

    /// <summary>
    /// Gets the owning product identifier.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Gets the selected mockup template identifier.
    /// </summary>
    public Guid MockupTemplateId { get; private set; }

    /// <summary>
    /// Gets the render order.
    /// </summary>
    public int SequenceOrder { get; private set; } = 1;

    /// <summary>
    /// Gets the owning product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the selected mockup template.
    /// </summary>
    public MockupTemplate MockupTemplate { get; private set; } = null!;
}
