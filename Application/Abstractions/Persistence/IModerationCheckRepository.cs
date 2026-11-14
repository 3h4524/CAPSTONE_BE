using APCS.Application.Abstractions.Persistence.Dtos;
using APCS.Common.Models;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Searches <c>moderation_checks</c> for the admin log. A focused repository because the category filter reads the
/// <c>categories</c> jsonb column, which a provider-neutral <c>Query()</c> cannot express. Empty, not an error, while
/// the table does not exist yet.
/// </summary>
public interface IModerationCheckRepository
{
    Task<PagedResult<ModerationCheckRow>> SearchAsync(ModerationCheckSearch search, CancellationToken cancellationToken);
}
