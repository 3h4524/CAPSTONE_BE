using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant;

public interface IAssistantService
{
    Task<Result<AssistantChatSession>> StartChatAsync(
        SendAssistantMessageRequestDto request,
        AssistantCaller caller,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        AssistantChatSession session,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AssistantConversationSummaryResponseDto>>> ListConversationsAsync(
        CancellationToken cancellationToken = default);

    Task<Result<AssistantConversationDetailResponseDto>> GetConversationAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Result<AssistantActionResultResponseDto>> ConfirmActionAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Result<AssistantActionResultResponseDto>> RejectActionAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
