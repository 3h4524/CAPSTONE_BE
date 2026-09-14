using APCS.Application.Features.SupportTickets.Dtos.Request;
using APCS.Application.Features.SupportTickets.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.SupportTickets;

/// <summary>Provides Seller and administrator support-ticket use cases.</summary>
public interface ISupportTicketService
{
    Task<Result<SupportTicketSummaryResponseDto>> CreateAsync(
        CreateSupportTicketRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SupportTicketSummaryResponseDto>>> ListMineAsync(
        ListSupportTicketsRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<SupportTicketDetailResponseDto>> GetMineAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Result<SupportTicketReplyResponseDto>> ReplyAsync(
        Guid id,
        CreateTicketReplyRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<SupportTicketSummaryResponseDto>> RateAsync(
        Guid id,
        RateSupportTicketRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SupportTicketSummaryResponseDto>>> ListAdminAsync(
        ListSupportTicketsRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<SupportTicketDetailResponseDto>> GetAdminAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Result<SupportTicketDetailResponseDto>> UpdateAdminAsync(
        Guid id,
        UpdateSupportTicketRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<SupportTicketReplyResponseDto>> ReplyAdminAsync(
        Guid id,
        CreateAdminTicketReplyRequestDto request,
        CancellationToken cancellationToken = default);
}
