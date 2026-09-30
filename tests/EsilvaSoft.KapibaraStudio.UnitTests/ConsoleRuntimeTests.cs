using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class ConsoleRuntimeTests : IDisposable
{
    private MemoryWorkspaceRepository _repository = null!;
    private ConnectionProfile _primary = null!;
    private FakeSession _session = null!;
    private ConsoleRuntime _runtime = null!;
    private static readonly bool[] ProjectionFlags = [false, true, true, false, true, false, false];
    [SetUp] public async Task SetUp()
    {
        _repository = new(); _primary = ConnectionProfile.Create("Development", "mongodb://localhost", "CakeShop");
        await _repository.SaveAsync(_primary);
        await _repository.SaveAsync(ConnectionProfile.Create("Production Cluster", "mongodb://localhost", isReadOnly: true));
        _session = new();
        _runtime = new(_repository, _repository, new SessionConnectionSecretStore(), _session, _repository, _repository);
    }
    [TearDown] public void TearDown() { _repository.Dispose(); }
    public void Dispose() { _repository?.Dispose(); _session?.Dispose(); }
    private Task<ConsoleExecutionResult> Run(string script, CancellationToken token = default) =>
        _runtime.ExecuteAsync(new(_primary, "CakeShop", script), (_, _) => Task.FromResult(true), token);

    [Test]
    public async Task ConsoleRuntimeUsesTheConfiguredLanguageForFixedDiagnostics()
    {
        var localization = new LocalizationViewModel { Language = "en" };
        _runtime.SetLocalization(localization.Resolve);

        var exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _runtime.ExecuteAsync(new(_primary, "", "{}"), (_, _) => Task.FromResult(true)));

        Assert.That(exception!.Message, Is.EqualTo("Provide a database and a script up to 1 MB."));
    }

    [Test] public async Task ExecutesRealJavaScriptWithMultipleConnectionsAndIndexedNames()
    {
        var result = await Run("""
            const dev = getConnection("Development").getDatabase("CompanyDb").getCollection("Customers");
            const prod = ConnectionPool["Production Cluster"]["Company-Db"]["customer-history"];
            dev.find({Active: true}).sort({Name: 1}).skip(20).limit(10).project({Name: 1});
            prod.find({Name: "Eduardo"}).limit(10);
            db.Customers.findOne({Name: "Eduardo"});
            console.log("Customers:", db.Customers.countDocuments());
            """
        );
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.Multiple(() => {
            Assert.That(result.Results, Has.Count.EqualTo(3));
            Assert.That(result.ConnectionsUsed, Has.Count.EqualTo(2));
            Assert.That(_session.Operations[0].Database, Is.EqualTo("CompanyDb"));
            Assert.That(_session.Operations[1].Collection, Is.EqualTo("customer-history"));
            Assert.That(result.Messages, Does.Contain("Customers:"));
            Assert.That(result.Results[0].Documents, Has.Count.EqualTo(1));
        });
        var history = await _repository.GetConsoleHistoryAsync();
        Assert.That(history.Single().ConnectionsUsed, Has.Count.EqualTo(2));
    }
    [Test] public async Task CursorIsLazyAndExplicitMaterializationIsBounded()
    {
        var result = await Run("const cursor = db.Customers.find({}); cursor.limit(100000).toArray();");
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.That(_session.Operations, Has.Count.EqualTo(1));
        Assert.That(_session.Operations[0].ArgumentsJson, Does.Contain("\"limit\":100"));
    }
    [TestCase("ConnectionPool['Production Cluster'].Company.Customers.deleteMany({_id: 1})", "somente leitura")]
    [TestCase("db.Customers.aggregate([{$out: 'copy'}])", "$out")]
    [TestCase("db.Customers.deleteMany({})", "filtro não vazio")]
    [TestCase("db.Customers.dropIndex('_id_')", "protegida")]
    public async Task RejectsForbiddenOperationsBeforeDriver(string script, string error)
    {
        var result = await Run(script);
        Assert.That(result.Error, Does.Contain(error));
        Assert.That(_session.Operations, Is.Empty);
    }
    [Test] public async Task ConfirmationIsPerActualDestinationAndDenialNeverReachesDriver()
    {
        ConsoleWriteConfirmation? asked = null;
        var result = await _runtime.ExecuteAsync(new(_primary, "CakeShop", "db.Customers.deleteOne({_id: 1})"), (request, _) => { asked = request; return Task.FromResult(false); });
        Assert.That(result.Error, Does.Contain("não confirmada"));
        Assert.That(asked!.Context, Does.Contain("Development › CakeShop › Customers"));
        Assert.That(_session.Operations, Is.Empty);
    }
    [Test] public async Task EnvironmentSnapshotAndExtendedJsonRemainStableDuringExecution()
    {
        var vault = _repository.LoadEnvironments(); vault.Environments[0].Values["KEY"] = "old";
        _repository.SaveEnvironments(vault);
        _session.Before = () => { vault.Environments[0].Values["KEY"] = "new"; _repository.SaveEnvironments(vault); };
        var result = await Run("db.Customers.findOne({}); ENV.get('KEY'); NumberLong('9223372036854775807');");
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.That(result.Results[1].Json, Is.EqualTo("\"old\""));
        Assert.That(result.Results[2].Json, Does.Contain("9223372036854775807"));
    }
    [Test] public async Task CancellationReachesDatabaseAndIsRecordedWithoutRollbackClaim()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.Wait = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        var running = Run("db.Customers.find({})", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        var result = await running;
        Assert.That(result.IsCanceled, Is.True);
        Assert.That((await _repository.GetConsoleHistoryAsync()).Single().Status, Is.EqualTo("Cancelado"));
    }
    [Test] public void StatementUsesParserForMultilineAndStringSemicolons()
    {
        const string script = "db.Customers.find({Name: 'a;b'})\n  .limit(10);\ndb.Orders.countDocuments()";
        var statement = _runtime.GetStatement(script, script.IndexOf(".limit", StringComparison.Ordinal));
        Assert.That(script.Substring(statement.Start, statement.Length), Is.EqualTo("db.Customers.find({Name: 'a;b'})\n  .limit(10);"));
        const string comment = "db.Customers.deleteOne({_id: 1});\n// não executar";
        Assert.That(_runtime.GetStatement(comment, comment.Length).Length, Is.Zero);
    }
    [Test] public async Task RuntimeHidesHostDelegatesAndSupportsSafeBsonArithmetic()
    {
        var result = await Run("typeof __hostCall; typeof System; typeof importNamespace; NumberLong('10') + 2; String(NumberLong('9223372036854775807')); EJSON.parse('{\"n\":{\"$numberInt\":\"7\"}}').n * 2;");
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.That(string.Join("|", result.Results.Select(r => r.Json)), Is.EqualTo("\"undefined\"|\"undefined\"|\"undefined\"|12|\"9223372036854775807\"|14"));
    }
    [Test] public async Task InvalidUnusedConnectionDoesNotBlockPrimaryAndHistoryOptOutIsHonored()
    {
        await _repository.SaveAsync(ConnectionProfile.Create("Missing", "mongodb://user:${ENV.get(\"absent_key_234232\")}@localhost"));
        var result = await _runtime.ExecuteAsync(new(_primary, "CakeShop", "db.Customers.find({})", SaveHistory: false), (_, _) => Task.FromResult(false));
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.That(await _repository.GetConsoleHistoryAsync(), Is.Empty);
    }
    [Test]
    public async Task ServerWaitTimeoutIsDistinctFromUserCancellation()
    {
        _session.Wait = token => Task.Delay(Timeout.Infinite, token);
        var result = await _runtime.ExecuteAsync(new(_primary, "CakeShop", "db.Customers.find({})", TimeoutMs: 100), (_, _) => Task.FromResult(false));
        Assert.That(result.IsTimedOut, Is.True);
        Assert.That(result.IsCanceled, Is.False);
        Assert.That(result.Error, Does.StartWith("Tempo limite excedido"));
    }

    [Test] public async Task InfiniteLoopIsBoundedAndSyntaxErrorNeverExecutesAnEarlierStatement()
    {
        var invalid = await Run("db.Customers.deleteOne({_id: 1}); invalid }");
        Assert.That(invalid.Error, Is.Not.Null);
        Assert.That(_session.Operations, Is.Empty);
        var loop = await _runtime.ExecuteAsync(new(_primary, "CakeShop", "while (true) {}", TimeoutMs: 100), (_, _) => Task.FromResult(false));
        Assert.That(loop.Error, Is.Not.Null);
        Assert.That(loop.Duration, Is.LessThan(TimeSpan.FromSeconds(3)));
    }
    [Test] public async Task ResultsKeepDriverFieldOrderAndReportProjectionUnlessTheScriptChangesThem()
    {
        // JavaScript enumerates integer-like keys first; the unmodified driver reply must keep the server order.
        _session.Reply = operation => operation.Method == "findOne"
            ? """{"value":{"b":1,"10":2}}"""
            : """{"value":[{"b":1,"10":2,"nome":"São"}],"truncated":true}""";
        var result = await Run("""
            db.C.find({});
            db.C.find({}, {nome: 1});
            db.C.find({}).project({nome: 1});
            db.C.find({}, {});
            db.C.findOne({}, {b: 1});
            db.C.aggregate([]);
            const changed = db.C.find({}).toArray();
            const ignored = (changed[0].b = 5);
            changed;
            """);
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.Multiple(() =>
        {
            Assert.That(result.Results, Has.Count.EqualTo(7));
            Assert.That(result.Results[0].Documents![0], Is.EqualTo("{\"b\":1,\"10\":2,\"nome\":\"São\"}"));
            Assert.That(result.Results[0].Json, Is.EqualTo("[{\"b\":1,\"10\":2,\"nome\":\"São\"}]"));
            Assert.That((result.Results[0].Method, result.Results[0].IsProjected, result.Results[0].IsTruncated), Is.EqualTo(("find", false, true)));
            Assert.That(result.Results.Select(r => r.IsProjected), Is.EqualTo(ProjectionFlags));
            Assert.That(result.Results[4].Documents!.Single(), Is.EqualTo("{\"b\":1,\"10\":2}"));
            Assert.That(result.Results[5].Method, Is.EqualTo("aggregate"));
            Assert.That(result.Results[6].Documents!.Single(), Does.StartWith("{\"10\":2").And.Contain("\"b\":5"), "A value changed by the script keeps the script output.");
        });
    }
    [Test]
    public async Task DateCallsAndConstructorsSendBsonDatesToTheDriver()
    {
        var result = await Run("""
            db.Customers.find({a: Date("2024-12-30 20:56:44.999"),
                b: new Date("2024-12-30T17:56:44.999-03:00"),
                c: ISODate("2024-12-30T20:56:44.999Z"),
                d: EJSON.parse('{"at":Date("2024-12-30 20:56:44.999")}').at});
            """);
        Assert.That(result.Error, Is.Null, result.Error);
        var args = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<MongoDB.Bson.BsonArray>(_session.Operations.Single().ArgumentsJson);
        var expected = new MongoDB.Bson.BsonDateTime(new DateTime(2024, 12, 30, 20, 56, 44, 999, DateTimeKind.Utc));
        foreach (var element in args[0].AsBsonDocument)
            Assert.That(element.Value, Is.EqualTo(expected), element.Name);
    }

    [Test]
    public async Task FailedSessionAcquisitionIsReportedAndNextExecutionCanRecover()
    {
        _session.CreateFailure = new InvalidOperationException("Sessão indisponível.");
        var failed = await Run("db.Customers.find({})");
        Assert.That(failed.Error, Is.EqualTo("Sessão indisponível."));
        Assert.That(_session.Operations, Is.Empty);

        _session.CreateFailure = null;
        var recovered = await Run("db.Customers.find({})");
        Assert.That(recovered.Error, Is.Null, recovered.Error);
        Assert.That(recovered.Results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentExecutionsHaveIndependentCancellationAndCapturedDestinations()
    {
        var factory = new IndependentSessions();
        var runtime = new ConsoleRuntime(_repository, _repository, new SessionConnectionSecretStore(), factory, _repository, _repository);
        using var firstCancellation = new CancellationTokenSource();
        var first = runtime.ExecuteAsync(new(_primary, "First", "db.Customers.find({})", SaveHistory: false),
            (_, _) => Task.FromResult(true), firstCancellation.Token);
        await factory.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = runtime.ExecuteAsync(new(_primary, "Second", "db.Orders.find({})", SaveHistory: false),
            (_, _) => Task.FromResult(true));
        await factory.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        firstCancellation.Cancel();
        var firstResult = await first;
        factory.ReleaseSecond.SetResult();
        var secondResult = await second;

        Assert.Multiple(() =>
        {
            Assert.That(firstResult.IsCanceled, Is.True);
            Assert.That(firstResult.Error, Does.Contain("não são revertidos"));
            Assert.That(secondResult.Error, Is.Null, secondResult.Error);
            Assert.That(secondResult.Results.Single().Database, Is.EqualTo("Second"));
            Assert.That(secondResult.Results.Single().Collection, Is.EqualTo("Orders"));
            Assert.That(factory.DisposedCount, Is.EqualTo(2));
        });
    }

    private sealed class IndependentSessions : IConsoleDatabaseSessionFactory
    {
        private int _created;
        private int _disposed;
        public int DisposedCount => _disposed;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs) =>
            new Session(this, Interlocked.Increment(ref _created));

        private sealed class Session(IndependentSessions owner, int number) : IConsoleDatabaseSession
        {
            public async Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken)
            {
                if (number == 1)
                {
                    owner.FirstStarted.SetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                else
                {
                    owner.SecondStarted.SetResult();
                    await owner.ReleaseSecond.Task.WaitAsync(cancellationToken);
                }
                return """{"value":[{"name":"captured"}],"truncated":false}""";
            }

            public void Dispose() => Interlocked.Increment(ref owner._disposed);
        }
    }

    private sealed class FakeSession : IConsoleDatabaseSessionFactory, IConsoleDatabaseSession
    {
        public List<ConsoleOperation> Operations { get; } = [];
        public Action? Before { get; set; }
        public Func<CancellationToken, Task>? Wait { get; set; }
        public Func<ConsoleOperation, string>? Reply { get; set; }
        public Exception? CreateFailure { get; set; }
        public IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs) =>
            CreateFailure is { } exception ? throw exception : this;
        public async Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken)
        {
            Operations.Add(operation); Before?.Invoke();
            if (Wait is not null) await Wait(cancellationToken);
            if (Reply is not null) return Reply(operation);
            return operation.Method switch {
                "find" or "aggregate" => """{"value":[{"_id":{"$numberLong":"9223372036854775807"},"Name":"Eduardo"}],"truncated":false}""",
                "findOne" => """{"value":{"Name":"Eduardo"}}""",
                _ => """{"value":1532}"""
            };
        }
        public void Dispose() { }
    }
}
