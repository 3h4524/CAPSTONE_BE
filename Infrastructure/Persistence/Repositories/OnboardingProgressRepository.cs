using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>Reads and writes <c>user_onboarding_progress</c> and the checklist signals with plain SQL: the progress table is added by a Flyway migration, not scaffolded.</summary>
public sealed class OnboardingProgressRepository(string connectionString, ILogger logger) : IOnboardingProgressRepository
{
    private const string UndefinedTable = "42P01";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OnboardingProgressState?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT guide_progress::text, checklist_dismissed
                FROM public.user_onboarding_progress
                WHERE user_id = @user_id
                """,
                connection);
            command.Parameters.AddWithValue("user_id", userId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var guideProgress = JsonSerializer.Deserialize<IReadOnlyDictionary<string, GuideProgress>>(
                reader.GetString(0),
                JsonOptions) ?? new Dictionary<string, GuideProgress>(StringComparer.Ordinal);

            return new OnboardingProgressState(guideProgress, reader.GetBoolean(1));
        }
        catch (PostgresException exception) when (exception.SqlState == UndefinedTable)
        {
            logger.LogWarning("user_onboarding_progress does not exist yet; apply its migration. Onboarding progress is reported as empty.");
            return null;
        }
    }

    public async Task<bool> SaveAsync(Guid userId, OnboardingProgressState progress, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO public.user_onboarding_progress (user_id, guide_progress, checklist_dismissed, created_at, updated_at)
                VALUES (@user_id, @guide_progress, @checklist_dismissed, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                ON CONFLICT (user_id) DO UPDATE
                SET guide_progress = EXCLUDED.guide_progress,
                    checklist_dismissed = EXCLUDED.checklist_dismissed,
                    updated_at = CURRENT_TIMESTAMP
                """,
                connection);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.Add(new NpgsqlParameter("guide_progress", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(progress.GuideProgress, JsonOptions)
            });
            command.Parameters.AddWithValue("checklist_dismissed", progress.ChecklistDismissed);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == UndefinedTable)
        {
            logger.LogWarning("user_onboarding_progress does not exist yet; apply its migration. The onboarding progress change was not persisted.");
            return false;
        }
    }

    public async Task<OnboardingSignals> GetSignalsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                EXISTS (
                    SELECT 1 FROM public.api_keys k
                    WHERE k.user_id = @user_id AND k.is_active AND k.deleted_at IS NULL),
                EXISTS (
                    SELECT 1 FROM public.batches b
                    WHERE b.user_id = @user_id AND b.deleted_at IS NULL),
                EXISTS (
                    SELECT 1 FROM public.mockup_templates t
                    WHERE t.user_id = @user_id),
                EXISTS (
                    SELECT 1 FROM public.workflow_runs r
                    WHERE r.user_id = @user_id)
            """,
            connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new OnboardingSignals(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3));
    }
}
