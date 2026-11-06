using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class Feature
{
    public string Code { get; set; } = null!;

    public string Label { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string GroupName { get; set; } = null!;

    public string Kind { get; set; } = null!;

    public string? Unit { get; set; }

    public int? DefaultLimit { get; set; }

    public bool TrialAvailable { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
