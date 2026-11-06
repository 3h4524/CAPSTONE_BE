namespace APCS.Application.Features.Admin.Dtos.Response;

public sealed record SupportTicketDto(
    Guid Id,
    string UserName,
    string UserEmail,
    string Issue,
    string Status,
    string Priority
);

/// <param name="Name">The job's name, e.g. "Design run".</param>
/// <param name="ItemCount">Products the job works on: its own count, or else its batch's products.</param>
/// <param name="Status">The job's status.</param>
/// <param name="ProgressPercentage">How far it got, e.g. "40%"; a completed job is at 100%.</param>
/// <param name="UserName">The seller who started it.</param>
/// <param name="Tone">The status's colour.</param>
/// <param name="BatchName">The batch it runs for, which tells jobs of the same name apart.</param>
/// <param name="CreatedAt">When it was started.</param>
/// <param name="AiCostUsd">What its AI calls cost, in USD.</param>
public sealed record BatchJobDto(
    string Name,
    int ItemCount,
    string Status,
    string ProgressPercentage,
    string UserName,
    string Tone,
    string? BatchName = null,
    DateTime? CreatedAt = null,
    decimal AiCostUsd = 0
);
