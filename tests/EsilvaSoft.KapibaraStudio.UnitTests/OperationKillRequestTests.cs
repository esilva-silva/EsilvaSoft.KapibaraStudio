using EsilvaSoft.KapibaraStudio.Core;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class OperationKillRequestTests
{
    [Test]
    public void GetOperationIdAcceptsConfirmedPositiveInteger()
    {
        var request = new OperationKillRequest("12345", "12345");

        Assert.That(request.GetOperationId(), Is.EqualTo(12345));
    }

    [TestCase("0", "0")]
    [TestCase("-1", "-1")]
    [TestCase("abc", "abc")]
    [TestCase("123", "124")]
    public void GetOperationIdRejectsInvalidOrUnconfirmedValue(string operationId, string confirmation)
    {
        var request = new OperationKillRequest(operationId, confirmation);

        Assert.That(() => request.GetOperationId(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void FindsCurrentOperationByBsonExtendedJsonId()
    {
        const string json = """{"inprog":[{"opid":{"$numberLong":"12345"},"host":"server-a:27017","op":"query","ns":"catalog.orders","client":"127.0.0.1:50000","command":{"find":"orders","filter":{"state":"open"}}}]}""";

        var fingerprint = OperationKillRequest.FindFingerprint(json, 12345);

        Assert.That(fingerprint, Is.Not.Null.And.Length.EqualTo(64));
    }

    [Test]
    public void FingerprintIgnoresVolatileCountersButChangesWhenTargetCommandChanges()
    {
        using var original = JsonDocument.Parse("""{"opid":{"$numberLong":"12345"},"host":"server-a:27017","op":"query","ns":"catalog.orders","client":"client:1","command":{"find":"orders","filter":{"state":"open"}},"secs_running":2}""");
        using var elapsed = JsonDocument.Parse("""{"opid":{"$numberLong":"12345"},"host":"server-a:27017","op":"query","ns":"catalog.orders","client":"client:1","command":{"filter":{"state":"open"},"find":"orders"},"secs_running":20}""");
        using var changed = JsonDocument.Parse("""{"opid":{"$numberLong":"12345"},"host":"server-a:27017","op":"query","ns":"catalog.orders","client":"client:1","command":{"find":"orders","filter":{"state":"closed"}},"secs_running":2}""");
        Assert.That(OperationKillRequest.TryCreateFingerprint(original.RootElement, out var first), Is.True);
        Assert.That(OperationKillRequest.TryCreateFingerprint(elapsed.RootElement, out var same), Is.True);
        Assert.That(OperationKillRequest.TryCreateFingerprint(changed.RootElement, out var other), Is.True);

        Assert.That(same, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
    }

    [Test]
    public void RefusesMissingAmbiguousOrUnidentifiableCurrentOperation()
    {
        const string missing = """{"inprog":[{"opid":{"$numberLong":"12345"},"host":"server-a:27017","op":"query","ns":"db.c","command":{"find":"c"}}]}""";
        const string duplicate = """{"inprog":[{"opid":12345,"host":"server-a:27017","op":"query","command":{"find":"c"}},{"opid":12345,"host":"server-a:27017","op":"query","command":{"find":"c"}}]}""";
        const string unidentified = """{"inprog":[{"opid":12345,"host":"server-a:27017","ns":"db.c"}]}""";

        Assert.That(OperationKillRequest.FindFingerprint(missing, 999), Is.Null);
        Assert.That(OperationKillRequest.FindFingerprint(duplicate, 12345), Is.Null);
        Assert.That(OperationKillRequest.FindFingerprint(unidentified, 12345), Is.Null);
        Assert.That(() => new OperationKillRequest("12345", "12345").ValidateForCurrentOperation(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void MatchesOnlyTheOperationThatWasCapturedForConfirmation()
    {
        const string observed = """{"inprog":[{"opid":{"$numberLong":"12345"},"host":"server-a:27017","op":"query","ns":"catalog.orders","client":"client:1","command":{"find":"orders","filter":{"state":"open"}}}]}""";
        const string changed = """{"inprog":[{"opid":{"$numberLong":"12345"},"host":"server-a:27017","op":"query","ns":"catalog.orders","client":"client:1","command":{"find":"orders","filter":{"state":"closed"}}}]}""";
        const string absent = """{"inprog":[]}""";
        var fingerprint = OperationKillRequest.FindFingerprint(observed, 12345)!;
        var request = new OperationKillRequest("12345", "12345", fingerprint);

        Assert.That(request.MatchesCurrentOperation(observed), Is.True);
        Assert.That(request.MatchesCurrentOperation(changed), Is.False);
        Assert.That(request.MatchesCurrentOperation(absent), Is.False);
    }

    [Test]
    public void RefusesReusedIdWhenServerOrConnectionIdentityChanges()
    {
        const string observed = """{"inprog":[{"opid":12345,"host":"server-a:27017","connectionId":7,"op":"query","ns":"db.items","command":{"find":"items"}}]}""";
        const string differentServer = """{"inprog":[{"opid":12345,"host":"server-b:27017","connectionId":7,"op":"query","ns":"db.items","command":{"find":"items"}}]}""";
        const string differentConnection = """{"inprog":[{"opid":12345,"host":"server-a:27017","connectionId":8,"op":"query","ns":"db.items","command":{"find":"items"}}]}""";
        var request = new OperationKillRequest("12345", "12345",
            OperationKillRequest.FindFingerprint(observed, 12345)!);

        Assert.Multiple(() =>
        {
            Assert.That(request.MatchesCurrentOperation(observed), Is.True);
            Assert.That(request.MatchesCurrentOperation(differentServer), Is.False);
            Assert.That(request.MatchesCurrentOperation(differentConnection), Is.False);
        });
    }

    [Test]
    public void RefusesUnknownHostTruncatedOrMalformedSnapshot()
    {
        const string missingHost = """{"inprog":[{"opid":12345,"op":"query","command":{"find":"items"}}]}""";
        const string complete = """{"inprog":[{"opid":12345,"host":"server-a:27017","op":"query","command":{"find":"items"}}]}""";
        const string truncated = """{"inprog":[{"opid":12345,"host":"server-a:27017","op":"query","command":{"find":"items"}}],"truncated":true}""";

        Assert.Multiple(() =>
        {
            Assert.That(OperationKillRequest.FindFingerprint(missingHost, 12345), Is.Null);
            Assert.That(OperationKillRequest.FindFingerprint(truncated, 12345), Is.Null);
            Assert.That(OperationKillRequest.FindFingerprint("[]", 12345), Is.Null);
            Assert.That(OperationKillRequest.FindFingerprint("{\"inprog\":[null]}", 12345), Is.Null);
            Assert.That(OperationKillRequest.FindFingerprint(complete, 12345), Is.Not.Null);
        });
    }
}
