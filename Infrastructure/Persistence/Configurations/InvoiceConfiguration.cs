using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures invoice persistence.
/// </summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", table =>
        {
            table.HasCheckConstraint("ck_invoices_amounts", "amount_usd >= 0 AND tax_amount >= 0 AND total_amount >= 0");
            table.HasCheckConstraint("ck_invoices_due_date", "due_date >= invoice_date");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        // Keep required dependents consistent with the Subscription soft-delete filter.
        builder.HasQueryFilter(invoice => invoice.Subscription.DeletedAtUtc == null);

        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(40).IsRequired();
        builder.Property(invoice => invoice.AmountUsd).HasPrecision(10, 2).IsRequired();
        builder.Property(invoice => invoice.TaxAmount).HasPrecision(10, 2).HasDefaultValue(0m);
        builder.Property(invoice => invoice.TotalAmount).HasPrecision(10, 2).IsRequired();
        builder.Property(invoice => invoice.Status).HasMaxLength(24).HasDefaultValue("draft").IsRequired();
        builder.Property(invoice => invoice.Items).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(invoice => invoice.PdfUrl).HasColumnType("text");
        builder.Property(invoice => invoice.StripeInvoiceId).HasMaxLength(128);

        builder.HasIndex(invoice => invoice.InvoiceNumber).IsUnique();
        builder.HasIndex(invoice => invoice.SellerId);
        builder.HasIndex(invoice => invoice.Status);
        builder.HasIndex(invoice => invoice.CreatedAtUtc).IsDescending();

        builder.HasOne(invoice => invoice.Subscription)
            .WithMany(subscription => subscription.Invoices)
            .HasForeignKey(invoice => invoice.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Seller>()
            .WithMany(seller => seller.Invoices)
            .HasForeignKey(invoice => invoice.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
