using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalDirectoryLauncherTests
{
    [Test]
    public async Task MissingDirectoryIsRefusedWithoutCreatingIt()
    {
        using var workspace = new SyntheticDirectory();
        var missing = Path.Combine(workspace.Path, "does-not-exist");
        var accepted = await new LocalDirectoryLauncher().OpenAsync(missing);

        Assert.That(accepted, Is.False);
        Assert.That(Directory.Exists(missing), Is.False);
    }

    [Test]
    public async Task RelativeDirectoryIsRefusedInsteadOfUsingTheHostWorkingDirectory()
        => Assert.That(await new LocalDirectoryLauncher().OpenAsync("models"), Is.False);

    [Test]
    public void CancellationBeforeSubmissionDoesNotOpenTheNativeFileManager()
    {
        using var workspace = new SyntheticDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => new LocalDirectoryLauncher().OpenAsync(workspace.Path, cancellation.Token));
        Assert.That(Directory.Exists(workspace.Path), Is.True);
    }

    [Test]
    [Explicit("Abre uma pasta sintética no gerenciador nativo; exige desktop interativo e inspeção manual.")]
    [Category("NativeDesktop")]
    public async Task ExistingDirectoryCanBeSubmittedToTheNativeFileManager()
    {
        using var workspace = new SyntheticDirectory();

        Assert.That(await new LocalDirectoryLauncher().OpenAsync(workspace.Path), Is.True);
    }
}
