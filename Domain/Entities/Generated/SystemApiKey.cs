using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SystemApiKey
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = null!;

    public string Label { get; set; } = null!;

    public string KeyValueEncrypted { get; set; } = null!;

    public string KeyLast4 { get; set; } = null!;

    public string KeyId { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public bool? LastCheckSucceeded { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual SystemApiKeyProvider ProviderNavigation { get; set; } = null!;

    public virtual User? UpdatedByNavigation { get; set; }
}
