using System.Security.Cryptography;
using System.Text;

namespace APCS.Common.Helpers;

/// <summary>
/// Provides hashing helpers for secrets that must not be stored as plaintext.
/// </summary>
public static class HashHelper
{
    /// <summary>
    /// Computes a SHA-256 hash and encodes it as Base64.
    /// </summary>
    /// <param name="value">The value to hash.</param>
    /// <returns>The Base64-encoded SHA-256 hash.</returns>
    public static string ComputeSha256Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);

        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Compares two hashes using fixed-time comparison.
    /// </summary>
    /// <param name="left">The first hash.</param>
    /// <param name="right">The second hash.</param>
    /// <returns>True when both hashes match.</returns>
    public static bool FixedTimeEquals(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
