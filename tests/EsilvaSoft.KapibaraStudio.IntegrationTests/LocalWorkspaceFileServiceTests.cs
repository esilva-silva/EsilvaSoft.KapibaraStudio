using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class LocalWorkspaceFileServiceTests
{
    [Test]
    public async Task ListCreateAndRenameStayInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"slop-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var service = new LocalWorkspaceFileService();
            await service.CreateDirectoryAsync(root, "src");
            var created = await service.CreateFileAsync(root, Path.Combine("src", "main.txt"));
            var renamed = await service.RenameAsync(root, Path.Combine("src", "main.txt"), "app.txt");
            Assert.That(renamed, Is.EqualTo(Path.Combine(root, "src", "app.txt")));
            Assert.That((await service.ListAsync(root, "src")).Single().Name, Is.EqualTo("app.txt"));
            Assert.That(created, Does.Contain("main.txt"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void TraversalAndRootMutationAreRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"slop-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var service = new LocalWorkspaceFileService();
            Assert.That(() => service.CreateFileAsync(root, ".." + Path.DirectorySeparatorChar + "escape.txt"), Throws.TypeOf<UnauthorizedAccessException>());
            Assert.That(() => service.CreateDirectoryAsync(root, "."), Throws.TypeOf<ArgumentException>());
        }
        finally { Directory.Delete(root, true); }
    }
}
