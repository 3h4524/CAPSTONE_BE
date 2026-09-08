using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PrintifyIntegration
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string PrintifyStoreId { get; set; } = null!;

    public string PrintifyApiTokenEncrypted { get; set; } = null!;

    public string ShopName { get; set; } = null!;

    public string? ShopTitle { get; set; }

    public bool? IsDefault { get; set; }

    public int? TotalProductsUploaded { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<PrintifyUploadLog> PrintifyUploadLogs { get; set; } = new List<PrintifyUploadLog>();

    public virtual User User { get; set; } = null!;
}
