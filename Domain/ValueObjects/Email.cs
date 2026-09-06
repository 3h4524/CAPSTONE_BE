using System.Text.RegularExpressions;
using APCS.Common.Constants;
using APCS.Domain.Exceptions;

namespace APCS.Domain.ValueObjects;

/// <summary>
/// Represents a validated email address.
/// </summary>
/// <param name="Value">The normalized email value.</param>
public sealed record Email(string Value)
{
    /// <summary>
    /// Creates an email value object after validation.
    /// </summary>
    public static Email Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Email cannot be empty.");
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(normalized, RegexPatterns.Email, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        {
            throw new DomainException("Email is not valid.");
        }

        return new Email(normalized);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
