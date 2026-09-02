using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures notification alert persistence.
/// </summary>
public sealed class NotificationAlertConfiguration : IEntityTypeConfiguration<NotificationAlert>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationAlert> builder)
    {
        builder.ToTable("notification_alerts");
        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(alert => alert.Type).HasMaxLength(64).IsRequired();
        builder.Property(alert => alert.Title).HasMaxLength(150).IsRequired();
        builder.Property(alert => alert.Message).HasColumnType("text").IsRequired();
        builder.Property(alert => alert.Severity).HasMaxLength(16).IsRequired();
        builder.Property(alert => alert.NotificationChannels)
            .HasColumnType("character varying(32)[]")
            .HasDefaultValueSql("ARRAY['in_app']::character varying(32)[]")
            .IsRequired();
        builder.Property(alert => alert.IsRead).HasDefaultValue(false);
        builder.Property(alert => alert.ReadAtUtc).HasColumnName("read_at");
        builder.Property(alert => alert.ActionUrl).HasMaxLength(500);
        builder.Property(alert => alert.ExpiresAtUtc).HasColumnName("expires_at");

        builder.HasIndex(alert => alert.SellerId);
        builder.HasIndex(alert => alert.IsRead);
        builder.HasIndex(alert => alert.Severity);
        builder.HasIndex(alert => alert.CreatedAtUtc).IsDescending();

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(alert => alert.SellerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(alert => alert.BatchJob)
            .WithMany()
            .HasForeignKey(alert => alert.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures support ticket persistence.
/// </summary>
public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("support_tickets", table =>
        {
            table.HasCheckConstraint("ck_support_tickets_satisfaction", "satisfaction_rating IS NULL OR satisfaction_rating BETWEEN 1 AND 5");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(ticket => ticket.TicketNumber).HasMaxLength(32).IsRequired();
        builder.Property(ticket => ticket.Subject).HasMaxLength(180).IsRequired();
        builder.Property(ticket => ticket.Description).HasColumnType("text").IsRequired();
        builder.Property(ticket => ticket.Category).HasMaxLength(64).IsRequired();
        builder.Property(ticket => ticket.Priority).HasMaxLength(16).HasDefaultValue("normal").IsRequired();
        builder.Property(ticket => ticket.AssignedToAdminId).HasColumnName("assigned_to");
        builder.Property(ticket => ticket.Status).HasMaxLength(24).HasDefaultValue("open").IsRequired();
        builder.Property(ticket => ticket.AttachmentUrls).HasColumnType("text[]");
        builder.Property(ticket => ticket.ResolvedAtUtc).HasColumnName("resolved_at");

        builder.HasIndex(ticket => ticket.TicketNumber).IsUnique();
        builder.HasIndex(ticket => ticket.SellerId);
        builder.HasIndex(ticket => ticket.Status);
        builder.HasIndex(ticket => ticket.Priority);
        builder.HasIndex(ticket => ticket.CreatedAtUtc).IsDescending();

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(ticket => ticket.SellerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ticket => ticket.AssignedToAdmin)
            .WithMany(admin => admin.AssignedTickets)
            .HasForeignKey(ticket => ticket.AssignedToAdminId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures support ticket reply persistence.
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

        builder.Property(reply => reply.AuthorRole).HasMaxLength(24).IsRequired();
        builder.Property(reply => reply.ReplyText).HasColumnType("text").IsRequired();
        builder.Property(reply => reply.AttachmentUrls).HasColumnType("text[]");
        builder.Property(reply => reply.IsInternalNote).HasDefaultValue(false);

        builder.HasIndex(reply => reply.SupportTicketId);
        builder.HasIndex(reply => reply.AuthorId);

        builder.HasOne(reply => reply.SupportTicket)
            .WithMany(ticket => ticket.Replies)
            .HasForeignKey(reply => reply.SupportTicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(reply => reply.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
