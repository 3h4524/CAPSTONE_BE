namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// A generated invoice PDF ready to stream back to the client (UC61 Download Invoice).
/// </summary>
public sealed record InvoiceFileDto(byte[] Content, string FileName);
