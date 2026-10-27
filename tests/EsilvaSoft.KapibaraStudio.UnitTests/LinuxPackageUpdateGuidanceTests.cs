using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed class LinuxPackageUpdateGuidanceTests
{
    [TestCase("apt", LinuxPackageManager.Apt)]
    [TestCase("dnf\n", LinuxPackageManager.Dnf)]
    [TestCase("pacman\r\n", LinuxPackageManager.Pacman)]
    [TestCase("  apt  \n\n", LinuxPackageManager.Apt)]
    public void MarkerWrittenByThePackagesIsRecognizedWithSurroundingWhitespace(string text, LinuxPackageManager expected)
    {
        Assert.That(LinuxPackageUpdateGuidance.TryParseMarker(text, out var manager), Is.True);
        Assert.That(manager, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   \n")]
    [TestCase("APT")]
    [TestCase("Pacman")]
    [TestCase("yum")]
    [TestCase("apt\ndnf")]
    [TestCase("apt dnf")]
    [TestCase("apt-get")]
    public void AnyOtherMarkerContentIsInvalidSoTheInstallationNeverReplacesItself(string? text) =>
        Assert.That(LinuxPackageUpdateGuidance.TryParseMarker(text, out _), Is.False, text);

    [TestCase(LinuxPackageManager.Apt, "linux-x64", "sudo apt install ./kapibarastudio_0.9.0_amd64.deb")]
    [TestCase(LinuxPackageManager.Apt, "linux-arm64", "sudo apt install ./kapibarastudio_0.9.0_arm64.deb")]
    [TestCase(LinuxPackageManager.Dnf, "linux-x64", "sudo dnf upgrade ./kapibarastudio-0.9.0-1.x86_64.rpm")]
    [TestCase(LinuxPackageManager.Dnf, "linux-arm64", "sudo dnf upgrade ./kapibarastudio-0.9.0-1.aarch64.rpm")]
    [TestCase(LinuxPackageManager.Pacman, "linux-x64", "tar -xzf kapibarastudio-bin-0.9.0-aur.tar.gz && cd kapibarastudio-bin && makepkg -si")]
    [TestCase(LinuxPackageManager.Pacman, "linux-arm64", "tar -xzf kapibarastudio-bin-0.9.0-aur.tar.gz && cd kapibarastudio-bin && makepkg -si")]
    public void CommandNamesThePackagePublishedForTheArchitecture(LinuxPackageManager manager, string rid, string expected) =>
        Assert.That(LinuxPackageUpdateGuidance.GetCommand(manager, AppVersion.Parse("0.9.0"), rid), Is.EqualTo(expected));

    [Test]
    public void PrereleaseFileNamesKeepTheSemVerTextBecauseGitHubRenamesTildesAndBuildMetadataIsDropped()
    {
        var beta = AppVersion.Parse("v1.2.3-beta.2+3f2a9c1");
        Assert.That(LinuxPackageUpdateGuidance.GetCommand(LinuxPackageManager.Apt, beta, "linux-x64"),
            Is.EqualTo("sudo apt install ./kapibarastudio_1.2.3-beta.2_amd64.deb"));
        Assert.That(LinuxPackageUpdateGuidance.GetCommand(LinuxPackageManager.Dnf, beta, "linux-arm64"),
            Is.EqualTo("sudo dnf upgrade ./kapibarastudio-1.2.3-beta.2-1.aarch64.rpm"));
        Assert.That(LinuxPackageUpdateGuidance.GetCommand(LinuxPackageManager.Pacman, beta, "linux-x64"),
            Is.EqualTo("tar -xzf kapibarastudio-bin-1.2.3-beta.2-aur.tar.gz && cd kapibarastudio-bin && makepkg -si"));
        Assert.That(LinuxPackageUpdateGuidance.GetCommand(LinuxPackageManager.Dnf, AppVersion.Parse("1.2.3+sha"), "linux-x64"),
            Is.EqualTo("sudo dnf upgrade ./kapibarastudio-1.2.3-1.x86_64.rpm"));
    }

    [TestCase("win-x64")]
    [TestCase("linux-musl-x64")]
    [TestCase("osx-arm64")]
    [TestCase("")]
    public void RuntimeWithoutLinuxPackageIsRejected(string rid)
    {
        foreach (var manager in Enum.GetValues<LinuxPackageManager>())
            Assert.Throws<ArgumentException>(() => LinuxPackageUpdateGuidance.GetCommand(manager, AppVersion.Parse("0.9.0"), rid), $"{manager} {rid}");
    }

    [Test]
    public void DisplayNameIdentifiesTheManagerTheUserRecognizes()
    {
        Assert.That(LinuxPackageUpdateGuidance.GetDisplayName(LinuxPackageManager.Apt), Is.EqualTo("APT"));
        Assert.That(LinuxPackageUpdateGuidance.GetDisplayName(LinuxPackageManager.Dnf), Is.EqualTo("DNF"));
        Assert.That(LinuxPackageUpdateGuidance.GetDisplayName(LinuxPackageManager.Pacman), Is.EqualTo("pacman/AUR"));
    }
}
