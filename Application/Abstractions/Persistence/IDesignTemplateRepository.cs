using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>Provides ownership-aware design-template persistence operations.</summary>
public interface IDesignTemplateRepository
{
    /// <summary>Lists active system templates without tracking.</summary>
    Task<IReadOnlyList<DesignTemplate>> ListSystemAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists active personal templates owned by a seller without tracking.</summary>
    Task<IReadOnlyList<DesignTemplate>> ListPersonalAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists active personal-template names owned by a seller for clone-name allocation.</summary>
    Task<IReadOnlyList<string>> ListPersonalNamesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a visible active template without tracking.</summary>
    Task<DesignTemplate?> GetVisibleAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an active template for an ownership-checked write.</summary>
    Task<DesignTemplate?> GetForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Checks a normalized active personal-template name.</summary>
    Task<bool> PersonalNameExistsAsync(
        Guid userId,
        string normalizedName,
        Guid? excludedId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Checks whether a template is referenced by a non-terminal batch job.</summary>
    Task<bool> IsUsedByActiveBatchAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds and saves a template, returning false for the personal-name unique constraint.</summary>
    Task<bool> TryAddAsync(
        DesignTemplate template,
        CancellationToken cancellationToken = default);

    /// <summary>Saves tracked template changes, returning false for the personal-name unique constraint.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Atomically increments usage after a prompt has been resolved successfully.</summary>
    Task IncrementUsageCountAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);
}
