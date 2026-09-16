using APCS.Application.Features.Subscriptions.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Renders invoices as PDFs with QuestPDF (UC61 Download Invoice).
/// </summary>
public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    static QuestPdfInvoiceRenderer()
    {
        // The Community license is free for organizations under QuestPDF's revenue threshold,
        // which covers this project; required once per process before any document is generated.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public byte[] Render(InvoicePdfModel model)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(style => style.FontSize(11));

                page.Header().Column(header =>
                {
                    header.Item().Text("APCS").FontSize(20).Bold();
                    header.Item().Text("AI POD Content Studio").FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(20).Column(content =>
                {
                    content.Spacing(8);

                    content.Item().Text($"Invoice {model.InvoiceNumber}").FontSize(16).Bold();
                    content.Item().Text($"Issued {model.IssuedDate:MMMM d, yyyy}");
                    content.Item().Text($"Status: {model.Status}").FontColor(Colors.Grey.Darken2);

                    content.Item().PaddingTop(12).Row(row =>
                    {
                        row.RelativeItem().Column(billedTo =>
                        {
                            billedTo.Item().Text("Billed To").Bold();
                            billedTo.Item().Text(model.BilledToName);
                            billedTo.Item().Text(model.BilledToEmail);
                        });

                        row.RelativeItem().Column(from =>
                        {
                            from.Item().Text("From").Bold();
                            from.Item().Text("APCS");
                            from.Item().Text("support@apcs.app");
                        });
                    });

                    content.Item().PaddingTop(16).BorderBottom(1).BorderColor(Colors.Grey.Lighten1)
                        .PaddingBottom(6).Row(row =>
                        {
                            row.RelativeItem(3).Text("Description").Bold();
                            row.RelativeItem(1).AlignRight().Text("Amount").Bold();
                        });

                    content.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem(3).Text($"{model.PlanName} plan — {model.BillingPeriodLabel}");
                        row.RelativeItem(1).AlignRight().Text($"${model.PlanPrice:0.00}");
                    });

                    content.Item().PaddingTop(16).AlignRight()
                        .Text($"Total Paid: ${model.TotalPaid:0.00}").FontSize(13).Bold();
                });

                page.Footer().AlignCenter()
                    .Text("Sample invoice generated for the APCS prototype.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf();
    }
}
