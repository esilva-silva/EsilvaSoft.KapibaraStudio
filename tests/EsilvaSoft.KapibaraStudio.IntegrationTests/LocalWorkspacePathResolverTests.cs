using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalWorkspacePathResolverTests
{
    [Test]
    public void WindowsUsesLocalApplicationDataInsteadOfXdgOrHome()
    {
        var data = SyntheticPaths.Combine("local-app-data");
        var paths = new LocalWorkspacePathResolver(true, data, SyntheticPaths.Combine("xdg"), SyntheticPaths.Combine("home"));

        Assert.That(paths.GetDatabasePath(), Is.EqualTo(Path.Combine(data, "EsilvaSoft", "KapibaraStudio", "workspace.db")));
    }

    [Test]
    public void LinuxUsesTheDeclaredXdgDataDirectory()
    {
        var data = SyntheticPaths.Combine("xdg");
        var paths = new LocalWorkspacePathResolver(false, SyntheticPaths.Combine("local-app-data"), data, SyntheticPaths.Combine("home"));

        Assert.That(paths.GetDatabasePath(), Is.EqualTo(Path.Combine(data, "EsilvaSoft", "KapibaraStudio", "workspace.db")));
    }

    [TestCase(null)]
    [TestCase("")]
    public void LinuxWithoutXdgUsesTheHomeDataDirectory(string? xdg)
    {
        var home = SyntheticPaths.Combine("home");
        var paths = new LocalWorkspacePathResolver(false, "unused", xdg, home);

        Assert.That(paths.GetDatabasePath(), Is.EqualTo(Path.Combine(home, ".local", "share", "EsilvaSoft", "KapibaraStudio", "workspace.db")));
    }

    [TestCase(true, "", null, "")]
    [TestCase(true, "relative", null, "")]
    [TestCase(false, "unused", "relative", "")]
    [TestCase(false, "unused", null, "")]
    public void UnresolvedOrRelativeHostRootsFailClosed(bool windows, string localData, string? xdg, string home)
        => Assert.Throws<InvalidOperationException>(() => new LocalWorkspacePathResolver(windows, localData, xdg, home));

    [Test]
    public void CurrentHostAdapterMatchesItsPlatformDataRoot()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : string.IsNullOrEmpty(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                : xdg;
        var directory = Path.Combine(root, "EsilvaSoft", "KapibaraStudio");
        var paths = new LocalWorkspacePathResolver();

        Assert.Multiple(() =>
        {
            Assert.That(paths.GetDatabasePath(), Is.EqualTo(Path.Combine(directory, "workspace.db")));
            Assert.That(paths.GetModelsDirectory(), Is.EqualTo(Path.Combine(directory, "Models")));
            Assert.That(paths.GetUpdatesDirectory(), Is.EqualTo(Path.Combine(directory, "updates")));
            Assert.That(paths.GetExportsDirectory(), Is.EqualTo(Path.Combine(directory, "exports")));
        });
    }
}
