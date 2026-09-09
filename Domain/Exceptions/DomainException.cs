namespace APCS.Domain.Exceptions;

/// <summary>
/// Represents a domain invariant violation.
/// </summary>
public sealed class DomainException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class.
    /// </summary>
    public DomainException(string message)
        : base(message)
    {
    }
}
