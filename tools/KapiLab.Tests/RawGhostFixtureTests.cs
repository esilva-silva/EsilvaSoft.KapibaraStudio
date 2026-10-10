using System.Security.Cryptography;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class RawGhostFixtureTests
{
    [Test]
    public void CheckedInSyntheticInputMatchesTheOptInAllowlistHash()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "autocomplete-raw-ghost-v1.jsonl");
        var bytes = File.ReadAllBytes(path);

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo(RawGhostFixture.Sha256));
            Assert.That(RawGhostFixture.IsVerified(bytes), Is.True);
            Assert.That(RawGhostFixture.IsVerified([.. bytes, (byte)' ']), Is.False);
        });
    }
}
