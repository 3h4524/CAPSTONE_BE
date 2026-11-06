using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>
/// Records a change to a plan in <c>audit_logs</c>, for the History tab. The entry is saved with
/// the caller's other changes, so it exists only if they do.
/// </summary>
public interface IPlanChangeLog
{
    Task AddAsync(string action, Guid? planId, object? before, object? after, CancellationToken cancellationToken = default);
}

/// <summary>The <c>audit_logs.action_type</c> values for plan rows themselves.</summary>
public static class PlanChangeActions
{
    public const string Create = "CreatePlan";
    public const string Update = "UpdatePlan";
    public const string Delete = "DeletePlan";
}

/// <summary>What the History tab shows of a plan row: its name, prices, run limits and whether it is on sale.</summary>
public sealed record PlanSnapshot(string Name, string Tier, decimal MonthlyPriceUsd, decimal? AnnualPriceUsd, int MaxBatchSize, int MaxConcurrentJobs, bool IsActive)
{
    public static PlanSnapshot Of(SubscriptionPlan plan) =>
        new(plan.Name, plan.Tier, plan.MonthlyPriceUsd, plan.AnnualPriceUsd, plan.MaxBatchSize, plan.MaxConcurrentJobs, plan.IsActive == true);
}

/// <inheritdoc />
public sealed class PlanChangeLog(IRepository<AuditLog> auditLogs, ICurrentUser currentUser, TimeProvider timeProvider) : IPlanChangeLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task AddAsync(string action, Guid? planId, object? before, object? after, CancellationToken cancellationToken = default) =>
        auditLogs.AddAsync(
            new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = currentUser.UserId,
                ActionType = action,
                ResourceType = "SubscriptionPlan",
                ResourceId = planId,
                OldValue = before is null ? null : JsonSerializer.Serialize(before, Json),
                NewValue = after is null ? null : JsonSerializer.Serialize(after, Json),
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime
            },
            cancellationToken: cancellationToken);
}
