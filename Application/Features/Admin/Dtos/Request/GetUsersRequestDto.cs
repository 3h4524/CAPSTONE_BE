namespace APCS.Application.Features.Admin.Dtos.Request;

public record GetUsersRequestDto(
    int PageIndex = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    string? Role = null,
    string? Plan = null,
    string? Status = null,
    string? SortBy = "LatestJoined",
    bool SortDesc = true
);
