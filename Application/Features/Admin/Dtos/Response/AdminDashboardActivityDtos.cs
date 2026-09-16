namespace APCS.Application.Features.Admin.Dtos.Response;

public sealed record MonthlyRevenueDto(
    string Month,
    decimal Subscriptions,
    decimal Usage
);

public sealed record SupportTicketDto(
    string UserName,
    string UserEmail,
    string Issue,
    string Status,
    string Priority
);

public sealed record BatchJobDto(
    string Name,
    int ItemCount,
    string Status,
    string ProgressPercentage,
    string UserName,
    string Tone
);
