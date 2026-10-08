using System.Text;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class AgentWorkspaceFileCreationIntegrationTests
{
    [Test]
    public async Task CreatesUtf8ContentAndNeverOverwritesExistingFile()
    {
        using var workspace = new Workspace();
        var creator = new LocalAgentWorkspaceFileCreator();
        const string content = "{\"ação\":\"capivara 🦫\"}\r\n";
        Assert.That(await creator.CreateAsync(workspace.Root, "novo.json", content, [], Allow, default), Is.EqualTo(AgentFileCreationStatus.Created));
        Assert.That(File.ReadAllBytes(Path.Combine(workspace.Root, "novo.json")), Is.EqualTo(new UTF8Encoding(false).GetBytes(content)));
        Assert.That(await creator.CreateAsync(workspace.Root, "novo.json", "replace", [], Allow, default), Is.EqualTo(AgentFileCreationStatus.AlreadyExists));
        Assert.That(File.ReadAllText(Path.Combine(workspace.Root, "novo.json")), Is.EqualTo(content));
        Assert.That(Directory.GetFiles(workspace.Root, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task ConcurrentSamePathHasOneWinnerAndCancellationLeavesNoPartialFile()
    {
        using var workspace = new Workspace();
        var creator = new LocalAgentWorkspaceFileCreator();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = creator.CreateAsync(workspace.Root, "same.txt", "first", [], async token =>
        { entered.SetResult(); await release.Task.WaitAsync(token); return true; }, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = creator.CreateAsync(workspace.Root, "same.txt", "second", [], Allow, default);
        try { release.SetResult(); Assert.That(await first, Is.EqualTo(AgentFileCreationStatus.Created)); }
        finally { release.TrySetResult(); }
        Assert.That(await second, Is.EqualTo(AgentFileCreationStatus.AlreadyExists));
        Assert.That(File.ReadAllText(Path.Combine(workspace.Root, "same.txt")), Is.EqualTo("first"));
        using var cancel = new CancellationTokenSource();
        Assert.That(async () => await creator.CreateAsync(workspace.Root, "cancel.txt", "private", [], token =>
        { cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(true); }, cancel.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(File.Exists(Path.Combine(workspace.Root, "cancel.txt")), Is.False);
        Assert.That(Directory.GetFiles(workspace.Root, "*.tmp"), Is.Empty);
        Assert.That(await creator.CreateAsync(workspace.Root, "cancel.txt", "recovered", [], Allow, default), Is.EqualTo(AgentFileCreationStatus.Created));
    }

    [TestCase("../escape.txt", AgentFileCreationStatus.PathRejected)]
    [TestCase(".env", AgentFileCreationStatus.PathRejected)]
    [TestCase("missing/file.txt", AgentFileCreationStatus.ParentMissing)]
    public async Task RefusesUnsafeExcludedOrMissingParentWithoutPublishing(string path, AgentFileCreationStatus expected)
    {
        using var workspace = new Workspace();
        Assert.That(await new LocalAgentWorkspaceFileCreator().CreateAsync(workspace.Root, path, "value",
            [".env"], Allow, default), Is.EqualTo(expected));
        Assert.That(Directory.GetFileSystemEntries(workspace.Root), Is.Empty);
    }

    [Test]
    public async Task AuthorizationRevokedAtCommitRemovesTemporaryAndNeverCreatesDestination()
    {
        using var workspace = new Workspace();
        var result = await new LocalAgentWorkspaceFileCreator().CreateAsync(workspace.Root, "revoked.txt", "private", [],
            _ => Task.FromResult(false), default);
        Assert.That(result, Is.EqualTo(AgentFileCreationStatus.PermissionDenied));
        Assert.That(Directory.GetFileSystemEntries(workspace.Root), Is.Empty);
    }

    private static Task<bool> Allow(CancellationToken _) => Task.FromResult(true);
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KapibaraStudio.Tests", "create-file-" + Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
