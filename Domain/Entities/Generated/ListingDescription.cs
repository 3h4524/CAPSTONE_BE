using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ListingDescription
{
    public Guid Id { get; set; }

    public Guid ListingContentId { get; set; }

    public string AiGeneratedDescription { get; set; } = null!;

    public string CurrentDescription { get; set; } = null!;

    public bool IsUserEdited { get; set; }

    public int WordCount { get; set; }

    public string StructureFollowed { get; set; } = null!;

    public bool KeywordDensityOptimal { get; set; }

    public decimal SeoScore { get; set; }

    public int VersionNumber { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public virtual ListingContent ListingContent { get; set; } = null!;
}
