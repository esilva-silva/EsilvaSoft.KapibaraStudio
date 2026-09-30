using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed class AgentActiveFileAttachmentAttestationTests
{
    private const string Buffer = "db.collection.find().limit(10);\n";

    [Test]
    public void MatchingRedactedAttachmentAndCapturedIdentityAreAttested()
    {
        var request = CreateRequest(Buffer, "tab-a", 4, "tab-a", 4);

        Assert.Multiple(() =>
        {
            Assert.That(AgentActiveFileAttachmentAttestation.HasActiveFileAttachment(request), Is.True);
            Assert.That(AgentActiveFileAttachmentAttestation.MatchesCapturedBuffer(request), Is.True);
        });
    }

    [Test]
    public void DivergentAttachmentContentIsNotAttested()
    {
        var request = CreateRequest("db.collection.drop();\n", "tab-a", 4, "tab-a", 4);

        Assert.That(AgentActiveFileAttachmentAttestation.MatchesCapturedBuffer(request), Is.False);
    }

    [TestCase("tab-b", 4)]
    [TestCase("tab-a", 5)]
    public void DifferentTabOrDocumentRevisionIsNotAttested(string requestTabId, long requestRevision)
    {
        var request = CreateRequest(Buffer, requestTabId, requestRevision, "tab-a", 4);

        Assert.That(AgentActiveFileAttachmentAttestation.MatchesCapturedBuffer(request), Is.False);
    }

    [Test]
    public void MissingDocumentRevisionIsNotAttested()
    {
        var request = CreateRequest(Buffer, "tab-a", 4, "tab-a", null);

        Assert.That(AgentActiveFileAttachmentAttestation.MatchesCapturedBuffer(request), Is.False);
    }

    private static AgentTurnRequest CreateRequest(string attachmentContent, string requestTabId, long requestRevision,
        string snapshotTabId, long? snapshotRevision)
    {
        var snapshot = new AgentWorkspaceContext(DateTimeOffset.UtcNow, TabId: snapshotTabId,
            DocumentVersion: snapshotRevision, BufferText: Buffer);
        return new AgentTurnRequest(AgentTurnId.New(), "revise the query", requestTabId, requestRevision)
        {
            WorkspaceContext = snapshot,
            Attachments = [new AgentContextAttachment(AgentAttachmentKind.ActiveFile, "consulta.js", "consulta.js",
                attachmentContent, System.Text.Encoding.UTF8.GetByteCount(attachmentContent), "")],
        };
    }
}
