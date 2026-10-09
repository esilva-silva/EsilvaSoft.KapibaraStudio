using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CurrentActivitySnapshotTests
{
    [Test]
    public void ProjectsOperationsSessionsLocksAndCommentDigestWithoutCommandBody()
    {
        const string response = """
            {"source":"$currentOp","inprog":[
              {"type":"op","opid":{"$numberLong":"42"},"host":"server-a:27017","op":"query","ns":"sample_mflix.movies","active":true,
               "secs_running":{"$numberInt":"3"},"waitingForLock":true,"locks":{"Global":"r","Collection":"w"},
               "command":{"find":"movies","filter":{"email":"private@example.test"},"comment":"batch-7"}},
              {"type":"idleSession","lsid":{"id":"opaque-session","uid":"private-uid"},
               "active":false,"locks":{"Database":"W"}}
            ]}
            """;

        var snapshot = CurrentActivitySnapshot.Parse(response);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Source, Is.EqualTo("$currentOp"));
            Assert.That(snapshot.Entries, Has.Count.EqualTo(2));
            Assert.That(snapshot.Entries[0].OperationId, Is.EqualTo(42));
            Assert.That(snapshot.Entries[0].Host, Is.EqualTo("server-a:27017"));
            Assert.That(snapshot.Entries[0].WaitingForLock, Is.True);
            Assert.That(snapshot.Entries[0].Locks, Is.EqualTo("Global:r,Collection:w"));
            Assert.That(snapshot.Entries[0].CommentTag, Is.EqualTo(CurrentActivitySnapshot.CommentTag("batch-7")));
            Assert.That(snapshot.Entries[1].Kind, Is.EqualTo("idleSession"));
            Assert.That(snapshot.Entries[1].SessionTag, Is.Not.Null);
            Assert.That(snapshot.ToString(), Does.Not.Contain("private@example.test")
                .And.Not.Contain("batch-7").And.Not.Contain("private-uid"));
        });
    }

    [Test]
    public void LimitsSnapshotWithoutKeepingRawArray()
    {
        var json = "{\"source\":\"currentOp (legacy)\",\"inprog\":["
            + string.Join(",", Enumerable.Repeat("{\"op\":\"query\"}", 201)) + "]}";

        var snapshot = CurrentActivitySnapshot.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Source, Is.EqualTo("currentOp (legacy)"));
            Assert.That(snapshot.Entries, Has.Count.EqualTo(CurrentActivitySnapshot.MaximumEntries));
            Assert.That(snapshot.LimitReached, Is.True);
        });
    }

    [Test]
    public void RejectsResponseWithoutInProgressArray()
    {
        Assert.That(() => CurrentActivitySnapshot.Parse("{\"inprog\":{}}"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void PreservesExplicitTruncationForBoundedServerSnapshot()
    {
        var snapshot = CurrentActivitySnapshot.Parse("{\"source\":\"$currentOp\",\"inprog\":[{\"op\":\"query\"}],\"truncated\":true}");

        Assert.That(snapshot.LimitReached, Is.True);
    }
}
