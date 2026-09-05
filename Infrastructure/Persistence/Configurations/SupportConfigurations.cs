using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures support tickets.
/// </summary>
public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("support_tickets", table =>
        {
            table.HasCheckConstraint("chk_support_tickets_priority", "priority IN ('low', 'normal', 'high', 'urgent')");
            table.HasCheckConstraint(
                "chk_support_tickets_status",
                "status IN ('open', 'in_progress', 'waiting_customer', 'resolved', 'closed')");
            table.HasCheckConstraint(
                "chk_support_tickets_rating",
                "satisfaction_rating IS NULL OR satisfaction_rating BETWEEN 1 AND 5");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(ticket => ticket.TicketNumber).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(ticket => ticket.Subject).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(ticket => ticket.Description).HasColumnType("text").IsRequired();
        builder.Property(ticket => ticket.Category).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(ticket => ticket.Priority).HasMaxLength(ColumnLengths.Code).HasDefaultValue("normal").IsRequired();
        builder.Property(ticket => ticket.Status).HasMaxLength(ColumnLengths.Code).HasDefaultValue("open").IsRequired();
        builder.Property(ticket => ticket.ResolvedAtUtc).HasColumnName("resolved_at");

        builder.HasIndex(ticket => ticket.TicketNumber).IsUnique();
        builder.HasIndex(ticket => ticket.UserId);
        builder.HasIndex(ticket => ticket.Status);
        builder.HasIndex(ticket => ticket.Priority);
        builder.HasIndex(ticket => ticket.AssignedTo);
        builder.HasIndex(ticket => ticket.CreatedAtUtc);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ticket => ticket.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Staff are ordinary users holding an admin role, so assignment points at users too.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ticket => ticket.AssignedTo)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures ticket replies.
/// </summary>
public sealed class TicketReplyConfiguration : IEntityTypeConfiguration<TicketReply>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TicketReply> builder)
    {
        builder.ToTable("ticket_replies");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(reply => reply.ReplyText).HasColumnType("text").IsRequired();
        builder.Property(reply => reply.IsInternalNote).HasDefaultValue(false);

        builder.HasIndex(reply => reply.SupportTicketId);
        builder.HasIndex(reply => reply.AuthorId);

        builder.HasOne(reply => reply.SupportTicket)
            .WithMany(ticket => ticket.Replies)
            .HasForeignKey(reply => reply.SupportTicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(reply => reply.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Configures files attached to tickets or replies.
/// </summary>
public sealed class TicketAttachmentConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("ticket_attachments", table =>
        {
            // Attached to the ticket or to one reply, never both and never neither.
            table.HasCheckConstraint(
                "chk_attachment_single_owner",
                "(support_ticket_id IS NOT NULL) <> (ticket_reply_id IS NOT NULL)");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(attachment => attachment.FileUrl).HasColumnType("text").IsRequired();
        builder.Property(attachment => attachment.FileName).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(attachment => attachment.MimeType).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(attachment => attachment.FileSizeMb).HasPrecision(10, 2).IsRequired();

        builder.HasIndex(attachment => attachment.SupportTicketId);
        builder.HasIndex(attachment => attachment.TicketReplyId);

        builder.HasOne(attachment => attachment.SupportTicket)
            .WithMany(ticket => ticket.Attachments)
            .HasForeignKey(attachment => attachment.SupportTicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(attachment => attachment.TicketReply)
            .WithMany(reply => reply.Attachments)
            .HasForeignKey(attachment => attachment.TicketReplyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(attachment => attachment.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Configures user notifications.
/// </summary>
public sealed class NotificationAlertConfiguration : IEntityTypeConfiguration<NotificationAlert>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationAlert> builder)
    {
        builder.ToTable("notification_alerts", table =>
        {
            table.HasCheckConstraint(
                "chk_notification_severity",
                "severity IN ('info', 'warning', 'error', 'critical')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(alert => alert.Type).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(alert => alert.Title).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(alert => alert.Message).HasColumnType("text").IsRequired();
        builder.Property(alert => alert.Severity).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(alert => alert.IsRead).HasDefaultValue(false);
        builder.Property(alert => alert.ReadAtUtc).HasColumnName("read_at");
        builder.Property(alert => alert.ActionUrl).HasMaxLength(ColumnLengths.StorageKey);
        builder.Property(alert => alert.ExpiresAtUtc).HasColumnName("expires_at");

        builder.HasIndex(alert => alert.UserId);
        builder.HasIndex(alert => alert.IsRead);
        builder.HasIndex(alert => alert.Severity);
        builder.HasIndex(alert => alert.CreatedAtUtc);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(alert => alert.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(alert => alert.BatchJob)
            .WithMany()
            .HasForeignKey(alert => alert.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures per-channel notification delivery attempts.
/// </summary>
public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries", table =>
        {
            table.HasCheckConstraint("chk_notification_channel", "channel IN ('in_app', 'email', 'webhook')");
            table.HasCheckConstraint(
                "chk_notification_delivery_status",
                "delivery_status IN ('pending', 'sent', 'failed', 'skipped')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(delivery => delivery.Channel).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(delivery => delivery.DeliveryStatus).HasMaxLength(ColumnLengths.Code).HasDefaultValue("pending").IsRequired();
        builder.Property(delivery => delivery.SentAtUtc).HasColumnName("sent_at");
        builder.Property(delivery => delivery.ErrorMessage).HasColumnType("text");
        builder.Property(delivery => delivery.RetryCount).HasDefaultValue(0);

        builder.HasIndex(delivery => new { delivery.NotificationAlertId, delivery.Channel })
            .HasDatabaseName("uq_notification_delivery_channel")
            .IsUnique();

        builder.HasIndex(delivery => delivery.DeliveryStatus);

        builder.HasQueryFilter(delivery => delivery.NotificationAlert.DeletedAtUtc == null);

        builder.HasOne(delivery => delivery.NotificationAlert)
            .WithMany(alert => alert.Deliveries)
            .HasForeignKey(delivery => delivery.NotificationAlertId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
