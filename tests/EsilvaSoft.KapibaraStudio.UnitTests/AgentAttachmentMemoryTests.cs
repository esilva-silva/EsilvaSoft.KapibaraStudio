using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class AgentAttachmentMemoryTests
{
    private static AgentProviderPermissions Permissions => AgentProviderPermissions.Default("claude-code") with
    {
        ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch,
        DataSending = new AgentDataSendingPermissions { ActiveFile = true }
    };

    [Test]
    public async Task UnsavedActiveBufferNeverUsesFileReader()
    {
        var files = new MemoryAgentFiles { ReadFailure = new IOException("Reading disk was not authorized.") };
        var context = new AgentWorkspaceContext(DateTimeOffset.UnixEpoch, BufferText: "db.items.find({});", TabId: "tab");
        var resolution = await AgentAttachmentResolver.ResolveAsync(
            [new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile)], context, Permissions, default, files);
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Succeeded, Is.True);
            Assert.That(resolution.Attachments.Single().Content, Is.EqualTo(context.BufferText));
            Assert.That(files.Reads, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingConsentOrPermissionRefusesBeforeReading(bool missingConsent)
    {
        var files = new MemoryAgentFiles();
        var permissions = missingConsent ? Permissions with { ExternalDestinationConsentAt = null }
            : Permissions with { DataSending = new AgentDataSendingPermissions { ActiveFile = false } };
        var resolution = await AgentAttachmentResolver.ResolveAsync(
            [new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile)],
            new AgentWorkspaceContext(DateTimeOffset.UnixEpoch, BufferText: "synthetic"), permissions, default, files);
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Failures.Single().Error, Is.EqualTo(missingConsent
                ? AgentAttachmentError.ConsentMissing : AgentAttachmentError.NotPermitted));
            Assert.That(files.Reads, Is.Zero);
        });
    }

    [Test]
    public async Task OversizedBufferIsRefusedWithoutReading()
    {
        var files = new MemoryAgentFiles();
        var resolution = await AgentAttachmentResolver.ResolveAsync(
            [new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile)], new AgentWorkspaceContext(
                DateTimeOffset.UnixEpoch, BufferText: new string('x', AgentAttachmentResolver.MaximumFileBytes + 1)),
            Permissions, default, files);
        Assert.That(resolution.Failures.Single().Error, Is.EqualTo(AgentAttachmentError.FileTooLarge));
        Assert.That(files.Reads, Is.Zero);
    }

    [Test]
    public void CancellationIsObservedBeforeReading()
    {
        var files = new MemoryAgentFiles();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await AgentAttachmentResolver.ResolveAsync(
            [new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile)],
            new AgentWorkspaceContext(DateTimeOffset.UnixEpoch, BufferText: "synthetic"), Permissions, cancellation.Token, files));
        Assert.That(files.Reads, Is.Zero);
    }
}
