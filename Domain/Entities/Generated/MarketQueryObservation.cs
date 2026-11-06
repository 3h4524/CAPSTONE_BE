using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class MarketQueryObservation
{
    public Guid Id { get; set; }

    public string Platform { get; set; } = null!;

    public string Query { get; set; } = null!;

    public DateOnly ObservedOn { get; set; }

    public int? TotalResults { get; set; }

    public int SampleSize { get; set; }

    public int LimitUsed { get; set; }

    public string SortOn { get; set; } = null!;

    public DateTime ObservedAt { get; set; }
}
