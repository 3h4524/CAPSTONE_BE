namespace APCS.Application.Features.Admin.Dtos.Request;

public record SuspendUserRequestDto(
    string Reason,
    int? DurationDays // null means permanent
);
