using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ListingTag
{
    public Guid Id { get; set; }

    public Guid ListingContentId { get; set; }

    public int TagCount { get; set; }

    public string TagTypeDistribution { get; set; } = null!;

    public decimal SeoScore { get; set; }

    public bool IsUserEdited { get; set; }

    public int VersionNumber { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public virtual ListingContent ListingContent { get; set; } = null!;

    public virtual ICollection<ListingTagItem> ListingTagItems { get; set; } = new List<ListingTagItem>();
}
