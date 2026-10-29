using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class Batch
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? DefaultNiche { get; set; }

    public string? DefaultProductType { get; set; }

    public string InputMethod { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<BatchJob> BatchJobs { get; set; } = new List<BatchJob>();

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    public virtual User User { get; set; } = null!;
}
