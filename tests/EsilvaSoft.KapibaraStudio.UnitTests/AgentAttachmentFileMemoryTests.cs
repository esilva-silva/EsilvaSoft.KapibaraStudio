using System.Text;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class AgentAttachmentFileMemoryTests
{
    private static string Root => SyntheticPaths.Combine("kapibara-memory", "workspace");
    private static AgentProviderPermissions Permissions => AgentProviderPermissions.Default("claude-code") with
    {
        ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch,
        DataSending = new AgentDataSendingPermissions { WorkspaceFiles = true },
        Workspace = new AgentWorkspacePermissions { UseFilesFolder = true }
    };
    private static Task<AgentAttachmentResolution> Resolve(MemoryAgentFiles files, string relative = "query.txt") =>
        AgentAttachmentResolver.ResolveAsync([new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, relative)],
            new AgentWorkspaceContext(DateTimeOffset.UnixEpoch, Root), Permissions, default, files, files);
    private static MemoryAgentFiles Files()
    {
        var files = new MemoryAgentFiles();
        files.AddDirectory(Root);
        return files;
    }

    [TestCase("utf8")]
    [TestCase("utf8bom")]
    [TestCase("utf16le")]
    [TestCase("utf16be")]
    public async Task FileBytesAreDecodedWithoutNativeResources(string encodingName)
    {
        var encoding = encodingName switch
        {
            "utf16le" => (Encoding)new UnicodeEncoding(false, true, true),
            "utf16be" => new UnicodeEncoding(true, true, true),
            "utf8bom" => new UTF8Encoding(true, true),
            _ => new UTF8Encoding(false, true)
        };
        var files = Files();
        files.Set(Path.Combine(Root, "query.txt"), [.. encoding.GetPreamble(), .. encoding.GetBytes("ação\n")]);
        var revision = files.Revision;
        var result = await Resolve(files);
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Attachments.Single().Content, Is.EqualTo("ação\n"));
            Assert.That(files.Reads, Is.EqualTo(1));
            Assert.That(files.Revision, Is.EqualTo(revision));
        });
    }

    [TestCase(false, AgentAttachmentError.NotFound)]
    [TestCase(true, AgentAttachmentError.FileTooLarge)]
    public async Task MissingOrOversizedFileHasTypedRefusal(bool oversized, AgentAttachmentError expected)
    {
        var files = Files();
        if (oversized) files.Set(Path.Combine(Root, "query.txt"), new byte[AgentAttachmentResolver.MaximumFileBytes + 5]);
        Assert.That((await Resolve(files)).Failures.Single().Error, Is.EqualTo(expected));
    }

    [Test]
    public async Task ReadFailureIsVisibleAsUnreadable()
    {
        var files = Files();
        files.ReadFailure = new IOException("Synthetic read failure.");
        Assert.That((await Resolve(files)).Failures.Single().Error, Is.EqualTo(AgentAttachmentError.Unreadable));
    }

    [TestCase(".env", AgentAttachmentError.Excluded)]
    [TestCase("../outside.txt", AgentAttachmentError.OutsideWorkspace)]
    [TestCase("SHORT~1.txt", AgentAttachmentError.UnsafePath)]
    public async Task UnsafeOrExcludedPathIsRefusedBeforeReading(string relative, AgentAttachmentError expected)
    {
        var files = Files();
        var result = await Resolve(files, relative);
        Assert.That(result.Failures.Single().Error, Is.EqualTo(expected));
        Assert.That(files.Reads, Is.Zero);
    }

    [Test]
    public async Task LinkedParentIsRefusedBeforeReading()
    {
        var files = Files();
        files.SetLink(Path.Combine(Root, "linked"));
        Assert.That((await Resolve(files, "linked/query.txt")).Failures.Single().Error, Is.EqualTo(AgentAttachmentError.OutsideWorkspace));
        Assert.That(files.Reads, Is.Zero);
    }

    [TestCase(false, AgentAttachmentError.NoWorkspace)]
    [TestCase(true, AgentAttachmentError.OutsideWorkspace)]
    public async Task PathInspectionFailureRefusesAccess(bool linkFailure, AgentAttachmentError expected)
    {
        var files = Files();
        if (linkFailure) files.LinkFailure = new IOException("Synthetic link inspection failure.");
        else files.DirectoryFailure = new UnauthorizedAccessException("Synthetic directory inspection failure.");
        Assert.That((await Resolve(files)).Failures.Single().Error, Is.EqualTo(expected));
        Assert.That(files.Reads, Is.Zero);
    }

    [Test]
    public async Task MissingProbeNeverFallsBackToNativeInspection()
    {
        var files = Files();
        var result = await AgentAttachmentResolver.ResolveAsync(
            [new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "query.txt")],
            new AgentWorkspaceContext(DateTimeOffset.UnixEpoch, Root), Permissions, default, files);
        Assert.That(result.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.NoWorkspace));
        Assert.That(files.Reads, Is.Zero);
    }

    [Test]
    public async Task FileReadAwaitDoesNotChangeTheCapturedChipListOrExclusions()
    {
        var files = Files();
        files.Set(Path.Combine(Root, "first.txt"), Encoding.UTF8.GetBytes("first"));
        var exclusions = new List<string> { "second.txt" };
        var permissions = Permissions with
        {
            Workspace = new AgentWorkspacePermissions { UseFilesFolder = true, Exclusions = exclusions },
        };
        var requests = new List<AgentAttachmentRequest>
        {
            new(AgentAttachmentKind.WorkspaceFile, "first.txt"),
            new(AgentAttachmentKind.WorkspaceFile, "second.txt"),
        };
        var reader = new FirstReadGate(files);
        var resolving = AgentAttachmentResolver.ResolveAsync(requests,
            new AgentWorkspaceContext(DateTimeOffset.UnixEpoch, Root), permissions, default, reader, files);

        await reader.FirstReadEntered.Task;
        exclusions.Clear();
        requests[1] = new AgentAttachmentRequest(AgentAttachmentKind.WorkspaceFile, "replacement.txt");
        reader.ReleaseFirstRead.TrySetResult();
        var result = await resolving;

        Assert.Multiple(() =>
        {
            Assert.That(result.Attachments, Has.Count.EqualTo(1));
            Assert.That(result.Attachments.Single().PathOrName, Is.EqualTo("first.txt"));
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.Excluded));
            Assert.That(reader.Paths, Has.Count.EqualTo(1));
            Assert.That(reader.Paths.Single(), Is.EqualTo(Path.Combine(Root, "first.txt")));
        });
    }

    private sealed class FirstReadGate(MemoryAgentFiles files) : IAgentBoundedFileReader
    {
        private int _readCount;
        public TaskCompletionSource FirstReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Paths { get; } = [];

        public AgentFileReadResult Read(string fullPath, int maximumBytes) => files.Read(fullPath, maximumBytes);

        public async Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken cancellationToken)
        {
            Paths.Add(fullPath);
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                FirstReadEntered.TrySetResult();
                await ReleaseFirstRead.Task.WaitAsync(cancellationToken);
            }

            return await files.ReadAsync(fullPath, maximumBytes, cancellationToken);
        }
    }
}
