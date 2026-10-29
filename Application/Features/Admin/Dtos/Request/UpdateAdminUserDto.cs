namespace APCS.Application.Features.Admin.Dtos.Request;

public record UpdateAdminUserDto(
    string FullName,
    string Email,
    DateTime? Birthday,
    string? AvatarUrl,
    string AccountStatus,
    IEnumerable<string> Roles
);
