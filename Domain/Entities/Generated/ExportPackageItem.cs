using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ExportPackageItem
{
    public Guid Id { get; set; }

    public Guid ExportPackageId { get; set; }

    public Guid ProductId { get; set; }

    public bool? IncludeDesignImages { get; set; }

    public bool? IncludeMockupImages { get; set; }

    public bool? IncludePromoVideo { get; set; }

    public bool? IncludeListingContent { get; set; }

    public string? FolderPathInZip { get; set; }

    public string ItemStatus { get; set; } = null!;

    public string? ErrorMessage { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ExportPackage ExportPackage { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}
