using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed class AppUpdateSelectorTests
{
    [Test]
    public void VersionsFollowSemanticPrecedenceAndIgnoreBuildMetadata()
    {
        string[] ordered = ["0.5.0", "0.5.1", "0.6.0-alpha", "0.6.0-beta.2", "0.6.0-beta.10", "0.6.0", "1.0.0"];
        for (var index = 1; index < ordered.Length; index++)
            Assert.That(AppVersion.Parse(ordered[index - 1]) < AppVersion.Parse(ordered[index]), Is.True, $"{ordered[index - 1]} < {ordered[index]}");
        Assert.That(AppVersion.Parse("v0.5.0+3f2a9c1"), Is.EqualTo(AppVersion.Parse("0.5.0")), "Publish adds the commit as build metadata.");
        Assert.That(AppVersion.Parse("0.6.0-beta.2").IsPrerelease, Is.True);
        Assert.That(AppVersion.Parse("0.6.0-beta.2").ToString(), Is.EqualTo("0.6.0-beta.2"));
        foreach (var invalid in new[] { "", "1.2", "1.2.3-", "01.2.3", "1.2.x", "1.2.3-beta..1", "1.2.3-beta.01" })
            Assert.That(AppVersion.TryParse(invalid, out _), Is.False, invalid);
    }

    [Test]
    public void StableInstallIgnoresPrereleasesWhilePrereleaseInstallFollowsThem()
    {
        AppReleaseCandidate[] releases = [Release("v0.5.1"), Release("v0.6.0-beta.1", prerelease: true), Release("v0.4.0")];
        Assert.That(AppUpdateSelector.SelectNewest(AppVersion.Parse("0.5.0"), releases, "win-x64")?.Tag, Is.EqualTo("v0.5.1"));
        Assert.That(AppUpdateSelector.SelectNewest(AppVersion.Parse("0.6.0-alpha"), releases, "win-x64")?.Tag, Is.EqualTo("v0.6.0-beta.1"));
        Assert.That(AppUpdateSelector.SelectNewest(AppVersion.Parse("0.6.0-alpha"), [.. releases, Release("v0.6.0")], "win-x64")?.Tag, Is.EqualTo("v0.6.0"));
        Assert.That(AppUpdateSelector.SelectNewest(AppVersion.Parse("0.5.1"), [Release("v0.7.0", prerelease: true)], "win-x64"), Is.Null,
            "A release flagged as pre-release on GitHub stays off the stable channel even without a suffix.");
    }

    [Test]
    public void DraftsSameVersionAndMissingRuntimePackageAreIgnored()
    {
        var current = AppVersion.Parse("0.5.0");
        AppReleaseCandidate[] releases = [Release("v0.6.0", draft: true), Release("v0.5.0"), Release("v0.7.0", rid: "linux-x64"), Release("latest")];
        Assert.That(AppUpdateSelector.SelectNewest(current, releases, "win-x64"), Is.Null);
        var linux = AppUpdateSelector.SelectNewest(current, releases, "linux-x64")!;
        Assert.That(linux.AssetName, Is.EqualTo("EsilvaSoft.SlopStudio-0.7.0-linux-x64.tar.gz"));
        Assert.That(linux.Sha256, Is.EqualTo(new string('a', 64)));
        Assert.That(linux.ChecksumsUrl, Is.Not.Null);
    }

    [Test]
    public void DigestsAndChecksumListingsAreNormalized()
    {
        Assert.That(AppUpdateSelector.NormalizeSha256("sha256:" + new string('A', 64)), Is.EqualTo(new string('a', 64)));
        Assert.That(AppUpdateSelector.NormalizeSha256("md5:abc"), Is.Null);
        Assert.That(AppUpdateSelector.NormalizeSha256("sha256:xyz"), Is.Null);
        var listing = $"{new string('b', 64)}  EsilvaSoft.KapibaraStudio-0.6.0-win-arm64.zip\n{new string('c', 64)} *EsilvaSoft.KapibaraStudio-0.6.0-win-x64.zip\n";
        Assert.That(AppUpdateSelector.FindChecksum(listing, "EsilvaSoft.KapibaraStudio-0.6.0-win-x64.zip"), Is.EqualTo(new string('c', 64)));
        Assert.That(AppUpdateSelector.FindChecksum(listing, "EsilvaSoft.KapibaraStudio-0.6.0-linux-x64.tar.gz"), Is.Null);
    }

    [TestCase("win-x64", "zip")]
    [TestCase("win-arm64", "zip")]
    [TestCase("linux-x64", "tar.gz")]
    [TestCase("linux-arm64", "tar.gz")]
    public void BothPackageNamesAreAcceptedAndKapibaraWinsWithinTheSameRelease(string rid, string extension)
    {
        var version = AppVersion.Parse("0.6.0");
        var legacyName = $"EsilvaSoft.SlopStudio-0.6.0-{rid}.{extension}";
        var newName = $"EsilvaSoft.KapibaraStudio-0.6.0-{rid}.{extension}";
        Assert.That(AppUpdateSelector.GetAssetNames(version, rid), Is.EqualTo(new[] { newName, legacyName }));
        var legacy = Release("v0.6.0", rid: rid);
        var modern = new AppReleaseAsset(newName, new Uri("https://downloads.test/kapibara"), 42, "sha256:" + new string('b', 64));
        foreach (var assets in new[] { new[] { modern }, legacy.Assets.Append(modern).ToArray(), legacy.Assets.Prepend(modern).ToArray() })
        {
            var release = legacy with { Assets = assets };
            var selected = AppUpdateSelector.SelectNewest(AppVersion.Parse("0.5.0"), [release], rid)!;
            Assert.That(selected.AssetName, Is.EqualTo(newName));
            Assert.That(selected.Sha256, Is.EqualTo(new string('b', 64)));
            Assert.That(selected.DownloadUrl, Is.EqualTo(modern.DownloadUrl));
        }
        Assert.That(AppUpdateSelector.SelectNewest(AppVersion.Parse("0.5.0"), [legacy], rid)!.AssetName, Is.EqualTo(legacyName));
    }

    private static AppReleaseCandidate Release(string tag, bool prerelease = false, bool draft = false, string rid = "win-x64")
    {
        var page = new Uri("https://github.test/releases/" + tag + "/");
        AppReleaseAsset[] assets = AppVersion.TryParse(tag, out var version)
            ? [new(AppUpdateSelector.GetAssetNames(version, rid)[1], new Uri(page, "package"), 10, "sha256:" + new string('a', 64)),
               new(AppUpdateSelector.ChecksumsAssetName, new Uri(page, "sums"), 1, null)]
            : [];
        return new(tag, draft, prerelease, page, assets);
    }
}
