using System.Security.Cryptography;
using System.Xml.Linq;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Time.Testing;

namespace APCS.Infrastructure.UnitTests.Services;

/// <summary>
/// The platform keys' encryption, with two servers sharing one ring the way two APIs share the database: a key saved
/// through one is read by the other.
/// </summary>
[TestClass]
public sealed class SystemApiKeySecretsTests
{
    [TestMethod]
    public void AKeySavedThroughOneServer_IsReadByAnother()
    {
        var ring = new SharedRing();
        using var first = new SystemApiKeySecrets(ring, new FakeTimeProvider());
        using var second = new SystemApiKeySecrets(ring, new FakeTimeProvider());

        var saved = first.Protect("ideogram-key-1234");

        saved.Should().NotContain("ideogram-key-1234");
        second.Unprotect(saved).Should().Be("ideogram-key-1234");
    }

    [TestMethod]
    public void AServerThatMadeARingKeyOfItsOwn_StillReadsAKeyEncryptedByTheOthersRingKey()
    {
        var ring = new SharedRing();
        using var first = new SystemApiKeySecrets(ring, new FakeTimeProvider());
        using var second = new SystemApiKeySecrets(ring, new FakeTimeProvider());
        // Both start on an empty ring at once: each makes its own ring key before seeing the other's.
        var bySecond = second.Protect("minimax-key-b");
        ring.Hidden = 1;
        var byFirst = first.Protect("minimax-key-a");
        ring.Hidden = 0;

        second.Unprotect(byFirst).Should().Be("minimax-key-a", "the ring is read again for a ring key it did not know");
        first.Unprotect(bySecond).Should().Be("minimax-key-b");
        ring.Elements.Should().HaveCount(2);
    }

    [TestMethod]
    public void TheRing_IsKeptReadableByEveryServer_NotEncryptedForThisMachine()
    {
        var ring = new SharedRing();
        using var secrets = new SystemApiKeySecrets(ring, new FakeTimeProvider());

        secrets.Protect("ideogram-key-1234");

        ring.Elements.Should().ContainSingle();
        ring.Elements[0].ToString().Should().NotContain("encryptedSecret", "Windows DPAPI would tie the ring to one machine");
    }

    [TestMethod]
    public void AKeyFromAnotherRing_CannotBeRead()
    {
        using var other = new SystemApiKeySecrets(new SharedRing(), new FakeTimeProvider());
        using var secrets = new SystemApiKeySecrets(new SharedRing(), new FakeTimeProvider());
        var foreign = other.Protect("ideogram-key-1234");

        var read = () => secrets.Unprotect(foreign);

        read.Should().Throw<CryptographicException>();
    }

    // The system_api_key_ring table, in memory. Hidden leaves the last rows out of reads: rows another server has not
    // seen yet.
    private sealed class SharedRing : IXmlRepository
    {
        public List<XElement> Elements { get; } = [];

        public int Hidden { get; set; }

        public IReadOnlyCollection<XElement> GetAllElements() =>
            Elements.Take(Math.Max(0, Elements.Count - Hidden)).Select(element => new XElement(element)).ToList();

        public void StoreElement(XElement element, string friendlyName) => Elements.Add(new XElement(element));
    }
}
