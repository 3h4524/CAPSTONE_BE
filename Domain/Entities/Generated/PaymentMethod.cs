using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PaymentMethod
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string PaymentType { get; set; } = null!;

    public string? StripePaymentMethodId { get; set; }

    public string? PaypalEmailEncrypted { get; set; }

    public string? CardLast4Digits { get; set; }

    public string? CardBrand { get; set; }

    public bool? IsDefault { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
