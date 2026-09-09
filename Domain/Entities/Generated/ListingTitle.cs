using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ListingTitle
{
    public Guid Id { get; set; }

    public Guid ListingContentId { get; set; }

    public string AiGeneratedTitle { get; set; } = null!;

    public string CurrentTitle { get; set; } = null!;

    public bool IsUserEdited { get; set; }

    public int CharacterCount { get; set; }

    public bool IncludesPrimaryKeyword { get; set; }

    public decimal SeoScore { get; set; }

    public int VersionNumber { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public virtual ListingContent ListingContent { get; set; } = null!;
}
