using System.Security.Cryptography;

namespace APCS.Common.Helpers;

/// <summary>
/// Provides AES-256-GCM encryption helpers for later secret storage use cases.
/// </summary>
public static class AesHelper
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM.
    /// </summary>
    /// <param name="plaintext">The plaintext bytes.</param>
    /// <param name="key">A 32-byte key.</param>
    /// <returns>A Base64 payload containing nonce, tag, and cipher text.</returns>
    public static string Encrypt(byte[] plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidateKey(key);

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var cipherText = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, cipherText, tag);

        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipherText).ToArray());
    }

    /// <summary>
    /// Decrypts an AES-256-GCM payload created by <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="payload">The Base64 payload.</param>
    /// <param name="key">A 32-byte key.</param>
    /// <returns>The decrypted plaintext bytes.</returns>
    public static byte[] Decrypt(string payload, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ValidateKey(key);

        var bytes = Convert.FromBase64String(payload);
        if (bytes.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Invalid AES-GCM payload.");
        }

        var nonce = bytes[..NonceSizeBytes];
        var tag = bytes[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var cipherText = bytes[(NonceSizeBytes + TagSizeBytes)..];
        var plaintext = new byte[cipherText.Length];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(nonce, cipherText, tag, plaintext);

        return plaintext;
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException("AES-256 requires a 32-byte key.", nameof(key));
        }
    }
}
