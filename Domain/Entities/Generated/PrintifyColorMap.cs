using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PrintifyColorMap
{
    public Guid Id { get; set; }

    public string Hex { get; set; } = null!;

    public string PrintifyName { get; set; } = null!;

    public long? BlueprintId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
