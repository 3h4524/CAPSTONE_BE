using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ProductMockupTemplate
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid MockupTemplateId { get; set; }

    public int SequenceOrder { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual MockupTemplate MockupTemplate { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}
