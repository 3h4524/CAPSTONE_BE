using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SystemApiKeyRing
{
    public Guid Id { get; set; }

    public string? FriendlyName { get; set; }

    public string Xml { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
