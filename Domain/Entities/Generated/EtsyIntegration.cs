using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class EtsyIntegration
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string EtsyShopId { get; set; } = null!;

    public string EtsyOauthTokenEncrypted { get; set; } = null!;

    public string? EtsyRefreshTokenEncrypted { get; set; }

    public string ShopName { get; set; } = null!;

    public bool? IsDefault { get; set; }

    public int? TotalListingsCreated { get; set; }

    public int? TotalListingsUpdated { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public DateTime? OauthExpiresAt { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<EtsyUploadLog> EtsyUploadLogs { get; set; } = new List<EtsyUploadLog>();

    public virtual User User { get; set; } = null!;
}
