using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ExportPackage
{
    public Guid Id { get; set; }

    public Guid? BatchJobId { get; set; }

    public Guid UserId { get; set; }

    public string Type { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? StorageProvider { get; set; }

    public string? StorageKey { get; set; }

    public string? DownloadUrl { get; set; }

    public decimal? FileSizeMb { get; set; }

    public decimal? CreationTimeSeconds { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public DateTime? DownloadedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public virtual BatchJob? BatchJob { get; set; }

    public virtual ICollection<ExportPackageItem> ExportPackageItems { get; set; } = new List<ExportPackageItem>();

    public virtual User User { get; set; } = null!;
}
