using System.Text.Json.Nodes;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LiteDbProfilerCaptureRecoveryTests
{
    [Test]
    public async Task PendingReceiptSurvivesRestartAndPreventsDuplicateCapture()
    {
        using var fixture = new Workspace();
        var ticket = Ticket();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            LiteDbConnectionProfileRepository repository = owner;
            await repository.CreateAsync(ticket);
            Assert.That(async () => await repository.CreateAsync(ticket with { Id = Guid.NewGuid() }),
                Throws.TypeOf<InvalidOperationException>());
            await repository.UpdateAsync(ticket with { State = ProfilerCaptureState.Active });
        }
        using (var reopened = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            LiteDbConnectionProfileRepository repository = reopened;
            var pending = await repository.GetPendingAsync();
            Assert.That(pending, Is.EqualTo(new[] { ticket with { State = ProfilerCaptureState.Active } }));
            await repository.CompleteAsync(ticket.Id);
            Assert.That(await repository.GetPendingAsync(), Is.Empty);
        }
    }

    [Test]
    public async Task FutureVersionIsPreservedAndBlocksNewCapture()
    {
        using var fixture = new Workspace();
        var ticket = Ticket();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await ((IProfilerCaptureRepository)owner).CreateAsync(ticket);
        using (var raw = fixture.OpenOffline())
        {
            var collection = raw.GetCollection("profilerCaptureRecoveryV1");
            var document = collection.FindById(ticket.Id.ToString("N"));
            var json = JsonNode.Parse(document["json"].AsString)!.AsObject();
            json[nameof(ProfilerCaptureTicket.Version)] = 99;
            document["json"] = json.ToJsonString();
            collection.Update(document);
        }
        using (var reopened = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            LiteDbConnectionProfileRepository repository = reopened;
            Assert.That(async () => await repository.GetPendingAsync(), Throws.TypeOf<InvalidDataException>());
            Assert.That(async () => await repository.CreateAsync(ticket with { Id = Guid.NewGuid() }),
                Throws.TypeOf<InvalidDataException>());
        }
        using var verify = fixture.OpenOffline();
        Assert.That(verify.GetCollection("profilerCaptureRecoveryV1")
            .FindById(ticket.Id.ToString("N"))["json"].AsString, Does.Contain("99"));
    }

    private static ProfilerCaptureTicket Ticket()
    {
        var now = DateTimeOffset.UtcNow;
        return new ProfilerCaptureTicket(1, Guid.NewGuid(), Guid.NewGuid(), "sample_mflix",
            new string('A', 64), now, now.AddMinutes(10),
            new ProfilerSettings(0, 100, 1m, null),
            new ProfilerSettings(1, 50, 0.5m, null), ProfilerCaptureState.Prepared);
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KapibaraStudio.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public LiteDatabase OpenOffline()
        {
            Directory.CreateDirectory(_directory);
            return new LiteDatabase($"Filename={Path};Connection=direct");
        }
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
