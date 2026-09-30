using System.Text;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class AgentProposalFileValidationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ClosedFileProposalUsesBoundedReaderAndPreservesSource(bool utf16)
    {
        using var directory = new SyntheticDirectory();
        var path = Path.Combine(directory.Path, "query.js");
        const string original = "db.a.find();\n";
        File.WriteAllText(path, original, utf16 ? Encoding.Unicode : new UTF8Encoding(false));
        var bytes = File.ReadAllBytes(path);
        var store = new AgentEditProposalStore(static action => action(), new LocalAgentBoundedFileReader());
        var result = store.Submit(Proposal(path, original));
        Assert.That(result.Status, Is.EqualTo(AgentEditProposalSubmissionStatus.Registered));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
    }

    [Test]
    public void ChangedFileRefusesCapturedBaseWithoutOverwritingSource()
    {
        using var directory = new SyntheticDirectory();
        var path = Path.Combine(directory.Path, "query.js");
        File.WriteAllText(path, "changed\n");
        var store = new AgentEditProposalStore(static action => action(), new LocalAgentBoundedFileReader());
        Assert.That(store.Submit(Proposal(path, "original\n")).Status, Is.EqualTo(AgentEditProposalSubmissionStatus.BaseChanged));
        Assert.That(File.ReadAllText(path), Is.EqualTo("changed\n"));
    }

    private static AgentEditProposal Proposal(string path, string original) => new(Guid.NewGuid(), Guid.NewGuid(), path,
        null, AgentEditProposalStore.Sha256(original), original, "proposed\n", [new AgentEditHunk(0, 0, [original], ["proposed"])],
        DateTimeOffset.UnixEpoch);
}
