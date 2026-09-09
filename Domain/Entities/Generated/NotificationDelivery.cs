using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class NotificationDelivery
{
    public Guid Id { get; set; }

    public Guid NotificationAlertId { get; set; }

    public string Channel { get; set; } = null!;

    public string DeliveryStatus { get; set; } = null!;

    public DateTime? SentAt { get; set; }

    public string? ErrorMessage { get; set; }

    public int? RetryCount { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual NotificationAlert NotificationAlert { get; set; } = null!;
}
