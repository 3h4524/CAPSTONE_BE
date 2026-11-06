using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class AssistantConversation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<AssistantMessage> AssistantMessages { get; set; } = new List<AssistantMessage>();

    public virtual ICollection<AssistantPendingAction> AssistantPendingActions { get; set; } = new List<AssistantPendingAction>();

    public virtual User User { get; set; } = null!;
}
