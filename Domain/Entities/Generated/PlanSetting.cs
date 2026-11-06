using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PlanSetting
{
    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual User? UpdatedByNavigation { get; set; }
}
