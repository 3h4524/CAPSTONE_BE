using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SystemApiKeyProvider
{
    public string Provider { get; set; } = null!;

    public int? DailyLimitPerKey { get; set; }

    public int? MaxConcurrentPerKey { get; set; }

    public string Strategy { get; set; } = null!;

    public Guid? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<SystemApiKey> SystemApiKeys { get; set; } = new List<SystemApiKey>();

    public virtual User? UpdatedByNavigation { get; set; }
}
