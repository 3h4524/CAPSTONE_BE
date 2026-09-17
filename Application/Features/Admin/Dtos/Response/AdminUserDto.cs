namespace APCS.Application.Features.Admin.Dtos.Response;

public record AdminUserDto(
    Guid Id,
    string FullName,
    string Email,
    string? AvatarUrl,
    string AccountStatus,
    IEnumerable<string> Roles,
    string Plan,
    int TotalJobs,
    decimal MonthlyApiCost,
    DateTime? CreatedAt
);
