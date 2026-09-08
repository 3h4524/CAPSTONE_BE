using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class MockupTemplate
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string ProductType { get; set; } = null!;

    public string BaseImageUrl { get; set; } = null!;

    public string? PreviewImageUrl { get; set; }

    public string PrintAreaConfig { get; set; } = null!;

    public int OutputWidthPx { get; set; }

    public int OutputHeightPx { get; set; }

    public bool? IsSystemTemplate { get; set; }

    public bool? IsActive { get; set; }

    public int? UsageCount { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<MockupImage> MockupImages { get; set; } = new List<MockupImage>();

    public virtual ICollection<ProductMockupTemplate> ProductMockupTemplates { get; set; } = new List<ProductMockupTemplate>();
}
