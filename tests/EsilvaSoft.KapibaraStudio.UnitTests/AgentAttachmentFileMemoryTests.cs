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
}
