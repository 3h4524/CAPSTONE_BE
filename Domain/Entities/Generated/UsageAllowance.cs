using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class UsageAllowance
{
    public string Scope { get; set; } = null!;

    public string Code { get; set; } = null!;

    public int LimitValue { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
