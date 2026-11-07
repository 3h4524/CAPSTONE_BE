using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ImageEvalRun
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Notes { get; set; }

    public int ImageCount { get; set; }

    public int ProductCount { get; set; }

    public int VersionCount { get; set; }

    public string Report { get; set; } = null!;

    public string? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}
