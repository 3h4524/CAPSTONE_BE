using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class MockupTemplatePrintifyLink
{
    public Guid MockupTemplateId { get; set; }

    public long BlueprintId { get; set; }

    public string BlueprintTitle { get; set; } = null!;

    public string? BlueprintImageUrl { get; set; }

    public long? PrintProviderId { get; set; }

    public string? PrintProviderTitle { get; set; }

    public List<string> DefaultSizes { get; set; } = null!;

    public Guid? UpdatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual MockupTemplate MockupTemplate { get; set; } = null!;

    public virtual User? UpdatedByNavigation { get; set; }
}
