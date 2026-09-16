namespace APCS.Application.Features.Subscriptions.Common;

/// <summary>
/// Renders an invoice as a downloadable PDF (UC61 Download Invoice).
/// </summary>
/// <remarks>
/// An Application-owned abstraction over the PDF rendering library, the same way
/// <see cref="IPaymentGatewayClient"/> keeps the PayOS SDK out of Application — only
/// Infrastructure's implementation depends on the third-party PDF package.
/// </remarks>
public interface IInvoicePdfRenderer
{
    /// <summary>
    /// Renders the given invoice detail as a PDF file's raw bytes.
    /// </summary>
    byte[] Render(InvoicePdfModel model);
}

/// <summary>
/// The invoice detail needed to render an Invoice Detail PDF (BR122): invoice number, issued
/// date, billing period, plan line item, total paid, status, and who it was billed to.
/// </summary>
public sealed record InvoicePdfModel(
    string InvoiceNumber,
    DateOnly IssuedDate,
    string BillingPeriodLabel,
    string BilledToName,
    string BilledToEmail,
    string PlanName,
    decimal PlanPrice,
    decimal TotalPaid,
    string Status);
