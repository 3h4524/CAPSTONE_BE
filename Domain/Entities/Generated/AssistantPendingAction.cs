using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class AssistantPendingAction
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid UserId { get; set; }

    public string ToolName { get; set; } = null!;

    public string Arguments { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Summary { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? Result { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public virtual AssistantConversation Conversation { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
