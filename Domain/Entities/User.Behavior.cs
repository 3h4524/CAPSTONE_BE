using APCS.Common.Constants;

namespace APCS.Domain.Entities;

/// <summary>
/// Adds authentication behavior without modifying the database-first generated type.
/// </summary>
public partial class User
{
    public bool CanAuthenticate =>
        DeletedAt is null
        && string.Equals(AccountStatus, AccountStatuses.Active, StringComparison.OrdinalIgnoreCase);
}
