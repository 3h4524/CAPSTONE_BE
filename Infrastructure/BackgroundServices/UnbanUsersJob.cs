using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class UnbanUsersJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UnbanUsersJob> _logger;
    private readonly TimeProvider _timeProvider;

    public UnbanUsersJob(
        IServiceProvider serviceProvider,
        ILogger<UnbanUsersJob> logger,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UnbanUsersJob started.");

        // Run every hour
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        try
        {
            // Initial run
            await ProcessUnbansAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ProcessUnbansAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("UnbanUsersJob is stopping.");
        }
    }

    private async Task ProcessUnbansAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var usersToUnban = await dbContext.Users
                .Where(u => u.AccountStatus == AccountStatuses.Suspended 
                            && u.SuspendedUntil != null 
                            && u.SuspendedUntil <= now)
                .ToListAsync(cancellationToken);

            if (usersToUnban.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Found {Count} users to unban.", usersToUnban.Count);

            foreach (var user in usersToUnban)
            {
                user.AccountStatus = AccountStatuses.Active;
                user.SuspendedUntil = null;
                user.UpdatedAt = now;

                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = null, // System
                    ActionType = "AutoUnlockUser_Job",
                    ResourceType = "User",
                    ResourceId = user.Id,
                    OldValue = "suspended",
                    NewValue = "active",
                    CreatedAt = now
                };
                
                dbContext.AuditLogs.Add(auditLog);

                _logger.LogInformation("User {UserId} automatically unbanned.", user.Id);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing unbans.");
        }
    }
}
