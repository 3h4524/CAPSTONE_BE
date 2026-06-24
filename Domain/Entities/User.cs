using APCS.Domain.Common;
using APCS.Domain.ValueObjects;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents the domain view of an APCS user.
/// </summary>
public sealed class User : AggregateRoot
{
    private User()
    {
    }

    private User(Guid id, Email email, string? fullName)
    {
        Id = id;
        Email = email;
        FullName = fullName;
    }

    /// <summary>
    /// Gets the user's email address.
    /// </summary>
    public Email Email { get; private set; } = null!;

    /// <summary>
    /// Gets the user's display name.
    /// </summary>
    public string? FullName { get; private set; }

    /// <summary>
    /// Creates a domain user.
    /// </summary>
    public static User Create(Guid id, Email email, string? fullName) => new(id, email, fullName);
}
