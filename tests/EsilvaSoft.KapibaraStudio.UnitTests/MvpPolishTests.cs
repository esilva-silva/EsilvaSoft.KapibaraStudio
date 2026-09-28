using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MvpPolishTests
{
    [Test]
    public void LargeDocumentFieldsLoadInBoundedGroupsWithoutDroppingJson()
    {
        var json = "{\"values\":[" + string.Join(',', Enumerable.Range(0, 1000)) + "]}";
        var document = new EsilvaSoft.KapibaraStudio.Desktop.ViewModels.ResultDocumentViewModel(json, 0);
        var fields = document.Fields.Single().Children;
        Assert.That(fields, Has.Count.EqualTo(257));
        Assert.That(fields[^1].Label, Does.StartWith("Próximos campos"));
        Assert.That(fields[^1].Children[0].Json, Is.EqualTo("256"));
        Assert.That(document.Json, Is.EqualTo(json));
    }

    [Test]
    public void ErrorMessagesCategorizeWithoutEchoingMongoCredentials()
    {
        var message = OperationErrorMessages.Describe(new FormatException("URI inválida mongodb://user:private@host/?token=secret"));
        Assert.That(message, Does.StartWith("Entrada inválida:").And.Not.Contain("private").And.Not.Contain("secret"));
        Assert.That(OperationErrorMessages.Describe(new TimeoutException()), Does.StartWith("Tempo limite excedido"));
        Assert.That(OperationErrorMessages.Describe(new JsonException()), Does.StartWith("JSON inválido"));
        Assert.That(OperationErrorMessages.Describe(new IOException(), export: true), Does.StartWith("Erro de exportação"));
    }

    [Test]
    public async Task FailedOrCanceledExportRemovesPartialAndPreservesExistingDestination()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mvp-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "result.json");
        var export = new LocalResultPageExportService();
        try
        {
            await File.WriteAllTextAsync(path, "original");
            Assert.ThrowsAsync<IOException>(async () => await export.ExportAsync(path, ["{}"], false));
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("original"));
            Assert.CatchAsync<JsonException>(async () => await export.ExportAsync(Path.Combine(directory, "bad.json"), ["{}", "{"], false));
            using var cancellation = new CancellationTokenSource();
            Assert.CatchAsync<OperationCanceledException>(async () => await export.ExportAsync(Path.Combine(directory, "cancel.json"), ["{}", "{}"], false,
                (_, _) => cancellation.Cancel(), cancellation.Token));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
            Assert.That(Path.GetFileName(Directory.GetFiles(directory)[0]), Is.EqualTo("result.json"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public async Task ConcurrentOperationsKeepPriorityAndCancelOnlySelectedWork()
    {
        var service = new ApplicationOperationService();
        using var query = service.Begin("Consulta", ApplicationOperationPriority.High);
        using var metadata = service.Begin("Sugestões", ApplicationOperationPriority.Low);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = query.Token.Register(() => canceled.TrySetResult());
        service.Cancel(query.Snapshot.Id);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() => {
            Assert.That(service.ActiveOperations[0].Id, Is.EqualTo(query.Snapshot.Id));
            Assert.That(service.ActiveOperations.Count, Is.EqualTo(2), "Cancellation must not hide work before it finishes.");
            Assert.That(metadata.Token.IsCancellationRequested, Is.False);
        });
        query.Complete(ApplicationOperationStatus.Cancelled);
        Assert.That(service.ActiveOperations.Single().Id, Is.EqualTo(metadata.Snapshot.Id));
        metadata.Complete();
        Assert.That(service.ActiveOperations, Is.Empty);
    }

    [Test]
    public async Task ParallelCompletionPreservesAllActiveEntriesAndBoundsTerminalRetention()
    {
        var service = new ApplicationOperationService();
        var work = Enumerable.Range(0, 100).Select(i => service.Begin(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        await Task.WhenAll(work.Select(operation => Task.Run(() => { operation.Report(50, 100); operation.Complete(); operation.Dispose(); })));
        Assert.That(service.ActiveOperations, Is.Empty);
        Assert.That(service.LastCompleted?.Status, Is.EqualTo(ApplicationOperationStatus.Success));
    }

    [Test]
    public async Task CsvPreservesColumnUnionNestedBsonEscapesAndFormulaPolicy()
    {
        using var stream = new MemoryStream();
        await QueryResultExportSerializer.WriteAsync(stream,
            ["{\"name\":\"Ana, \\\"B\\\"\\nC\",\"n\":-2,\"obj\":{\"$numberLong\":\"9223372036854775807\"}}", "{\"name\":\"=SUM(A1)\",\"empty\":\"\",\"nil\":null}"], true);
        var csv = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Multiple(() => {
            Assert.That(csv, Does.StartWith("\"name\",\"n\",\"obj\",\"empty\",\"nil\""));
            Assert.That(csv, Does.Contain("\"Ana, \"\"B\"\"\nC\",\"-2\""));
            Assert.That(csv, Does.Contain("{\"\"$numberLong\"\":\"\"9223372036854775807\"\"}"));
            Assert.That(csv, Does.Contain("\"'=SUM(A1)\",,,\"\",\"null\""));
        });
    }

    [Test]
    public async Task JsonExportWritesBeforeEntirePageAndPropagatesCancellation()
    {
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var observed = false;
        try
        {
            await QueryResultExportSerializer.WriteAsync(stream, ["{\"x\":1}", "{\"x\":2}"], false,
                (done, _) => { observed = stream.Length > 0; if (done == 1) cancellation.Cancel(); }, cancellation.Token);
            Assert.Fail("Export must propagate cancellation.");
        }
        catch (OperationCanceledException) { Assert.That(observed, Is.True); }
    }

    [TestCase("db.Users.find({\"Active\":true})")]
    [TestCase("const x={date:ISODate(\"2024-01-01T00:00:00Z\"),r:/[{},]/g}; // keep\nconsole.log(x);")]
    [TestCase("function f(){return {x:1,y:[2,3]};} const t=`raw { x } ${f()}`;")]
    public async Task FormattingPreservesTokensCommentsRegexTemplatesAndIsIdempotent(string source)
    {
        var formatter = new MongoCodeFormatter();
        var formatted = await formatter.FormatAsync(source);
        Assert.That(formatted, Does.Contain("\n"));
        Assert.That(await formatter.FormatAsync(formatted), Is.EqualTo(formatted));
        Assert.That(Tokens(formatted), Is.EqualTo(Tokens(source)));
        if (source.Contains("// keep", StringComparison.Ordinal)) Assert.That(formatted, Does.Contain("// keep\n"));
        if (source.Contains('`')) Assert.That(formatted, Does.Contain("`raw { x } ${f()}`"));
    }

    private static string[] Tokens(string source)
    {
        var tokens = new List<string>();
        new Acornima.Parser(new Acornima.ParserOptions { OnToken = (in Acornima.Token token) => tokens.Add(source[token.Start..token.End]) }).ParseScript(source);
        return tokens.ToArray();
    }

    [Test]
    public async Task FormattingInvalidAndCanceledTextDoesNotProduceOutput()
    {
        var formatter = new MongoCodeFormatter();
        Assert.CatchAsync<Acornima.ParseErrorException>(async () => await formatter.FormatAsync("db.find({"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await formatter.FormatAsync("{}", cancellation.Token));
        using var parsed = JsonDocument.Parse(await formatter.FormatAsync("{\"n\":9223372036854775807}"));
        Assert.That(parsed.RootElement.GetProperty("n").GetInt64(), Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void MongoClientsAreReusedOnlyForEquivalentSettings()
    {
        using var pool = new MongoClientPool();
        var first = pool.Get(MongoClientSettings.FromConnectionString("mongodb://localhost:27017/?appName=mvp-test"));
        var again = pool.Get(MongoClientSettings.FromConnectionString("mongodb://localhost:27017/?appName=mvp-test"));
        var changed = pool.Get(MongoClientSettings.FromConnectionString("mongodb://localhost:27017/?appName=mvp-other"));
        Assert.That(again, Is.SameAs(first));
        Assert.That(changed, Is.Not.SameAs(first));
    }
}
