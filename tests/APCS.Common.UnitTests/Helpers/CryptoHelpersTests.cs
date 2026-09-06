using System.Security.Cryptography;
using System.Text;
using APCS.Common.Helpers;
using FluentAssertions;

namespace APCS.Common.UnitTests.Helpers;

[TestClass]
public sealed class CryptoHelpersTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [TestMethod]
    public void EncryptAndDecrypt_WithValidInputs_RoundTripsPlaintext()
    {
        var plaintext = Encoding.UTF8.GetBytes("secret payload");

        var encrypted = AesHelper.Encrypt(plaintext, Key);
        var decrypted = AesHelper.Decrypt(encrypted, Key);

        decrypted.Should().Equal(plaintext);
    }

    [TestMethod]
    public void Encrypt_Twice_UsesDifferentNonces()
    {
        var plaintext = Encoding.UTF8.GetBytes("same payload");

        AesHelper.Encrypt(plaintext, Key).Should().NotBe(AesHelper.Encrypt(plaintext, Key));
    }

    [TestMethod]
    public void Decrypt_WhenPayloadIsTampered_ThrowsCryptographicException()
    {
        var bytes = Convert.FromBase64String(AesHelper.Encrypt([1, 2, 3], Key));
        bytes[^1] ^= 0xFF;

        var act = () => AesHelper.Decrypt(Convert.ToBase64String(bytes), Key);

        act.Should().Throw<CryptographicException>();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(16)]
    [DataRow(31)]
    [DataRow(33)]
    public void Encrypt_WhenKeyLengthIsInvalid_Throws(int keyLength)
    {
        var act = () => AesHelper.Encrypt([1], new byte[keyLength]);

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void ComputeSha256Hash_ForKnownValue_ReturnsExpectedBase64()
    {
        HashHelper.ComputeSha256Hash("abc")
            .Should().Be("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=");
    }

    [TestMethod]
    public void FixedTimeEquals_ForEqualAndDifferentValues_ReturnsExpected()
    {
        HashHelper.FixedTimeEquals("same", "same").Should().BeTrue();
        HashHelper.FixedTimeEquals("same", "diff").Should().BeFalse();
        HashHelper.FixedTimeEquals("short", "longer").Should().BeFalse();
    }
}
