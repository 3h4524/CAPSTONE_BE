using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class Permission
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Resource { get; set; } = null!;

    public string Action { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
