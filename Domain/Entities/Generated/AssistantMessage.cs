using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class AssistantMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public string Role { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string Metadata { get; set; } = null!;

    public string? Model { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AssistantConversation Conversation { get; set; } = null!;
}
