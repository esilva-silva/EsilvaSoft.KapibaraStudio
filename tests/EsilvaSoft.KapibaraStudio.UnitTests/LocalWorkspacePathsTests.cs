using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Desktop;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
[Category("Unit")]
public sealed class LocalWorkspacePathsTests
{
    [Test]
    public void DesktopCompositionWithoutPathsFailsBeforeRegisteringProviders()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => App.AddDesktopAgentServices(services, null!));
        Assert.That(services, Is.Empty);
    }

    [Test]
    public void AllLocationsBelongToTheExplicitWorkspaceDirectory()
    {
        var directory = SyntheticPaths.Combine("workspace-paths");
        var paths = new LocalWorkspacePaths(Path.Combine(directory, "workspace.db"));

        Assert.Multiple(() =>
        {
            Assert.That(paths.GetDatabasePath(), Is.EqualTo(Path.Combine(directory, "workspace.db")));
            Assert.That(paths.GetModelsDirectory(), Is.EqualTo(Path.Combine(directory, "Models")));
            Assert.That(paths.GetUpdatesDirectory(), Is.EqualTo(Path.Combine(directory, "updates")));
            Assert.That(paths.GetExportsDirectory(), Is.EqualTo(Path.Combine(directory, "exports")));
        });
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("workspace.db")]
    public void MissingWorkspaceDirectoryFailsClosed(string databasePath)
        => Assert.Throws<ArgumentException>(() => new LocalWorkspacePaths(databasePath));
}
