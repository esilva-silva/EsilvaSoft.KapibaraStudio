using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Hardening of the CLP-1 contracts: redaction markers, declared turn-plan capability and safe ToString.</summary>
[TestFixture]
public sealed class AgentContractsHardeningTests
{
    private const string Canary = "Canary7Secret";

    [Test]
    public void ProposalThatIntroducesARedactionMarkerIsDetected()
    {
        // The agent saw the redacted text and echoed the marker back: applying it would overwrite the real secret.
        var original = "const uri = \"mongodb://svc:" + Canary + "@db/shop\";\ndb.orders.find({})";
        var seenByAgent = AgentSecretRedaction.Redact(original)!;
        var proposed = seenByAgent.Replace("find({})", "find({ status: 1 })", StringComparison.Ordinal);
        var hunks = LineDiff.Compute(original, proposed);
        Assert.Multiple(() =>
        {
            Assert.That(AgentRedactionMarkers.IntroducesRedactionMarker(original, proposed), Is.True);
            Assert.That(hunks.Any(AgentRedactionMarkers.IntroducesRedactionMarker), Is.True);
            Assert.That(AgentRedactionMarkers.IntroducesRedactionMarker(seenByAgent, proposed), Is.False,
                "Marcador já presente no original não é novidade.");
            Assert.That(AgentRedactionMarkers.IntroducesRedactionMarker("a", "b"), Is.False);
        });
    }

    [Test]
    public void TurnPlanCapabilityIsOffByDefaultAndIntersected()
    {
        var declared = new AgentProviderCapabilities
        {
            Chat = true, TurnPlan = true, Evidence = AgentCapabilityEvidence.AutomatedContract,
        };
        Assert.Multiple(() =>
        {
            Assert.That(AgentProviderCapabilities.None.TurnPlan, Is.False);
            Assert.That(declared.Normalize().TurnPlan, Is.True);
            Assert.That((declared with { Chat = false }).Normalize().TurnPlan, Is.False, "TurnPlan exige Chat.");
            Assert.That(declared.IntersectWith(declared with { TurnPlan = false }).TurnPlan, Is.False);
            Assert.That(declared.IntersectWith(declared).TurnPlan, Is.True);
        });
    }

    [Test]
    public void ModeOfATurnComesFromItsPlan()
    {
        var plan = AgentTurnPlan.Blocked(AgentOperationMode.Planning, AgentTurnBlockReason.ConsentMissing);
        var request = new AgentTurnRequest(AgentTurnId.New(), "oi", "tab-1", 1) { Plan = plan };
        Assert.Multiple(() =>
        {
            Assert.That(request.Mode, Is.EqualTo(AgentOperationMode.Planning));
            Assert.That(new AgentTurnRequest(AgentTurnId.New(), "oi", "tab-1", 1).Mode, Is.Null);
        });
    }

    [Test]
    public void ToStringNeverCarriesUserContent()
    {
        var entry = new AgentConversationEntry(AgentConversationEntryKind.UserMessage, "texto " + Canary, DateTimeOffset.UnixEpoch);
        var conversation = new AgentConversation(Guid.NewGuid(), "claude-code", "título " + Canary, null,
            AgentOperationMode.Agent, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, [entry]);
        var attachment = new AgentContextAttachment(AgentAttachmentKind.WorkspaceFile, "a.js", "a.js", Canary, 13, "00");
        var workspace = new AgentWorkspaceContext(DateTimeOffset.UnixEpoch, BufferText: Canary);
        Assert.Multiple(() =>
        {
            Assert.That(entry.ToString(), Does.Not.Contain(Canary));
            Assert.That(conversation.ToString(), Does.Not.Contain(Canary));
            Assert.That(attachment.ToString(), Does.Not.Contain(Canary));
            Assert.That(workspace.ToString(), Does.Not.Contain(Canary));
        });
    }
}
