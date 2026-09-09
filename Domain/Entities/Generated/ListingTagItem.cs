using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ListingTagItem
{
    public Guid Id { get; set; }

    public Guid ListingTagId { get; set; }

    public string TagValue { get; set; } = null!;

    public string NormalizedValue { get; set; } = null!;

    public string TagType { get; set; } = null!;

    public int Position { get; set; }

    public string Source { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual ListingTag ListingTag { get; set; } = null!;
}
