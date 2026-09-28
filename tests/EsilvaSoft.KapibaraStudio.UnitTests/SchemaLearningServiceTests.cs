using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// L13: <see cref="SchemaLearningCoordinator"/>'s bounded queue/worker and <see cref="SchemaLearningService"/>'s
/// fire-and-forget hook, wired all the way from <see cref="WorkspaceTabViewModel"/> result delivery.
/// <para>
/// <strong>Coalescing.</strong> The coordinator has exactly one sequential worker (documented on the type itself):
/// two envelopes of the same key are never interleaved with a third key's envelope because nothing ever runs
/// concurrently. <see cref="TwoEnvelopesOfTheSameKeyAreNeverInterleavedWithAThirdKey"/> below is the regression for
/// that simplification, not a test of a per-key grouping step (there is none in this lote).
/// </para>
/// <para>
/// <strong>Duplicate deliveries.</strong> Redelivering the same query result (e.g. rerunning it) is <em>not</em>
/// deduplicated: schema-learning.md is explicit that repeating a find "produz novas observações, não prova
/// documentos únicos". <see cref="RedeliveringTheSameResultCountsAsTwoSeparateObservations"/> is the regression for
/// that decision, kept intentional rather than accidental.
/// </para>
/// </summary>
[TestFixture]
public sealed class SchemaLearningServiceTests
{
    private static readonly Guid ProfileId = Guid.NewGuid();
    private static readonly string[] OrdersThenOrdersThenCustomers = ["orders", "orders", "customers"];
    private static readonly string[] JustOrders = ["orders"];

    private static SchemaLearningCoordinator NewCoordinator(FakeLearnedSchemaRepository repository, int capacity = SchemaLearningCoordinator.DefaultCapacity) =>
        new(new BackgroundSchemaAnalyzer(new MetadataClock()), repository, capacity);

    private static StructuredResultSet CompleteFindResult(string database, string collection, params string[] documents)
    {
        var origin = new ResultOrigin("Consulta", ProfileId, null, database, collection);
        return StructuredResultSet.FromDocuments(1, origin, documents, false, ResultCompleteness.Complete, "find");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Test]
    public async Task TryEnqueueNeverThrowsEvenWhileTheWorkerIsPermanentlyStuck()
    {
        var repository = new FakeLearnedSchemaRepository { OnApply = (_, _) => new TaskCompletionSource<SchemaCommitResult>().Task };
        var coordinator = NewCoordinator(repository, capacity: 2);
        try
        {
            var admitted = Enumerable.Range(0, 10)
                .Select(i => SchemaLearningAdmissionPolicy.Evaluate(CompleteFindResult("shop", "orders", "{\"a\":" + i + "}"),
                    SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 1, 0), SchemaLearningPolicy.Default).Envelope!)
                .ToArray();

            // The first envelope is what gets picked up by the worker and gets it permanently stuck in OnApply;
            // only after that is confirmed does flooding the queue with the rest actually exercise a stuck worker.
            Assert.That(coordinator.TryEnqueue(admitted[0]), Is.True);
            Assert.That(await WaitUntilAsync(() => repository.ApplyCount >= 1), Is.True, "O primeiro lote precisa ter entrado em processamento (e ficado preso) para o teste valer algo.");

            Assert.DoesNotThrow(() =>
            {
                foreach (var envelope in admitted.Skip(1)) coordinator.TryEnqueue(envelope);
            });

            Assert.That(coordinator.DroppedCount, Is.GreaterThan(0), "Com o worker preso e a fila pequena, o excedente precisa ser descartado, não bloqueado.");
        }
        finally
        {
            await coordinator.StopAsync(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        }
    }

    [Test]
    public async Task FullQueueDropsTheNewestBatchWithoutTouchingTheAlreadyDeliveredResult()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FakeLearnedSchemaRepository { OnApply = (_, _) => new TaskCompletionSource<SchemaCommitResult>().Task };
        var coordinator = NewCoordinator(repository, capacity: 1);
        var service = new SchemaLearningService(coordinator);
        var mongo = DispatchProxy.Create<IMongoWorkspaceService, MongoTestProxy>();
        ((MongoTestProxy)mongo).Handler = (name, _) => name switch
        {
            "QueryAsync" => Task.FromResult(new QueryPage(["{\"_id\":1,\"nome\":\"Ana\"}"], TimeSpan.Zero, false)),
            _ => throw new NotSupportedException(name)
        };
        var workspace = new WorkspaceService(context.Repository, context.Repository, context.Repository, context.Repository, context.Repository,
            mongo, context.Scripts, new LocalScriptFileService(), new SessionConnectionSecretStore(), schemaLearning: service);
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        var tab = new WorkspaceTabViewModel(workspace) { Profile = profile, Database = "loja", Collection = "clientes", Mode = "Consulta JSON", IsConnected = true, Text = "{}" };

        await tab.ExecuteCommand.ExecuteAsync(null);
        var resultsAfterFirst = tab.Results;
        var documentsAfterFirst = tab.Documents.ToArray();
        Assert.That(await WaitUntilAsync(() => repository.ApplyCount >= 1), Is.True);

        // Every further delivery while the single slot is occupied by the stuck batch is a dropped new batch.
        for (var i = 0; i < 5; i++) await tab.ExecuteCommand.ExecuteAsync(null);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(coordinator.DroppedCount, Is.GreaterThan(0), "A fila cheia precisa descartar os lotes novos.");
                Assert.That(tab.Results, Is.EqualTo(resultsAfterFirst), "O resultado já entregue à aba não muda por causa da fila de aprendizado.");
                Assert.That(tab.Documents.ToArray(), Is.EqualTo(documentsAfterFirst));
                Assert.That(tab.Errors, Is.Empty, "Fila cheia não é erro de execução.");
            });
        }
        finally
        {
            await coordinator.StopAsync(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        }
    }

    [Test]
    public async Task AFaultingCommitDoesNotStopTheWorkerAndNeverLeaksAnUnobservedTaskException()
    {
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) { unobserved.Add(e.Exception); e.SetObserved(); }
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var repository = new FakeLearnedSchemaRepository
            {
                OnApply = (delta, _) => delta.CompleteDocumentObservations == 1
                    ? throw new OperationCanceledException("Simula cancelamento no meio da gravação.")
                    : Task.FromResult(new SchemaCommitResult(SchemaCommitOutcome.Applied, 1, null))
            };
            var coordinator = NewCoordinator(repository);
            try
            {
                var first = SchemaLearningAdmissionPolicy.Evaluate(CompleteFindResult("shop", "orders", "{\"a\":1}"),
                    SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 1, 0), SchemaLearningPolicy.Default).Envelope!;
                var second = SchemaLearningAdmissionPolicy.Evaluate(CompleteFindResult("shop", "orders", "{\"a\":1}", "{\"a\":2}"),
                    SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 1, 0), SchemaLearningPolicy.Default).Envelope!;

                Assert.That(coordinator.TryEnqueue(first), Is.True);
                Assert.That(await WaitUntilAsync(() => coordinator.FailedCount == 1), Is.True, "O primeiro lote deveria falhar e ser contado, sem derrubar o worker.");

                Assert.That(coordinator.TryEnqueue(second), Is.True);
                Assert.That(await WaitUntilAsync(() => coordinator.ProcessedCount == 1), Is.True, "O worker precisa continuar consumindo depois de uma falha.");
            }
            finally
            {
                await coordinator.StopAsync(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            Assert.That(unobserved, Is.Empty, "Nenhuma exceção de commit deve escapar como UnobservedTaskException.");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [Test]
    public async Task RedeliveringTheSameResultCountsAsTwoSeparateObservations()
    {
        var repository = new FakeLearnedSchemaRepository();
        var coordinator = NewCoordinator(repository);
        var service = new SchemaLearningService(coordinator);
        try
        {
            var resultSet = CompleteFindResult("shop", "orders", "{\"a\":1}");

            service.NotifyResultDelivered(resultSet, Guid.NewGuid(), 1, 0);
            service.NotifyResultDelivered(resultSet, Guid.NewGuid(), 1, 0);

            Assert.That(await WaitUntilAsync(() => repository.ApplyCount == 2), Is.True,
                "Reentregar o mesmo resultado produz duas observações; não é deduplicado por conteúdo.");
            Assert.That(repository.Applied.Select(delta => delta.BatchId).Distinct().Count(), Is.EqualTo(2),
                "Cada entrega tem seu próprio BatchId; não há BatchId compartilhado a idempotência de retry.");
        }
        finally
        {
            await coordinator.StopAsync();
        }
    }

    [TestCase(ResultCompleteness.Derived)]
    [TestCase(ResultCompleteness.PartialProjection)]
    [TestCase(ResultCompleteness.Unknown)]
    public void ResultsThatAreNotACompleteFindNeverReachTheRepository(ResultCompleteness completeness)
    {
        var repository = new FakeLearnedSchemaRepository();
        var coordinator = NewCoordinator(repository);
        var service = new SchemaLearningService(coordinator);
        try
        {
            var origin = new ResultOrigin("Consulta", ProfileId, null, "shop", "orders");
            var resultSet = StructuredResultSet.FromDocuments(1, origin, ["{\"a\":1}"], false, completeness,
                completeness == ResultCompleteness.Derived ? "aggregate" : "find");

            service.NotifyResultDelivered(resultSet, Guid.NewGuid(), 1, 0);

            // Admission is synchronous: a rejection is decided before TryEnqueue is even called, so there is
            // nothing to await here — the assertion is valid immediately.
            Assert.That(repository.ApplyCount, Is.Zero);
        }
        finally
        {
            coordinator.StopAsync().GetAwaiter().GetResult();
        }
    }

    [Test]
    public async Task DeliveringAResultTriggersNoAdditionalMongoQuery()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FakeLearnedSchemaRepository();
        var coordinator = NewCoordinator(repository);
        var service = new SchemaLearningService(coordinator);
        var queryCalls = 0;
        var mongo = DispatchProxy.Create<IMongoWorkspaceService, MongoTestProxy>();
        ((MongoTestProxy)mongo).Handler = (name, _) =>
        {
            if (name == "QueryAsync") Interlocked.Increment(ref queryCalls);
            return name switch
            {
                "QueryAsync" => Task.FromResult(new QueryPage(["{\"_id\":1,\"nome\":\"Ana\"}"], TimeSpan.Zero, false)),
                _ => throw new NotSupportedException(name)
            };
        };
        var workspace = new WorkspaceService(context.Repository, context.Repository, context.Repository, context.Repository, context.Repository,
            mongo, context.Scripts, new LocalScriptFileService(), new SessionConnectionSecretStore(), schemaLearning: service);
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        var tab = new WorkspaceTabViewModel(workspace) { Profile = profile, Database = "loja", Collection = "clientes", Mode = "Consulta JSON", IsConnected = true, Text = "{}" };

        try
        {
            await tab.ExecuteCommand.ExecuteAsync(null);
            Assert.That(await WaitUntilAsync(() => repository.ApplyCount == 1), Is.True, "O aprendizado precisa ter processado o lote para o teste provar algo.");
            Assert.That(queryCalls, Is.EqualTo(1), "Nenhuma consulta MongoDB adicional deve ser disparada pelo enfileiramento de aprendizado.");
        }
        finally
        {
            await coordinator.StopAsync();
        }
    }

    [Test]
    public void TwoEnvelopesOfTheSameKeyAreNeverInterleavedWithAThirdKey()
    {
        var repository = new FakeLearnedSchemaRepository();
        var order = new List<string>();
        var gate = new SemaphoreSlim(0);
        repository.OnApply = async (delta, _) =>
        {
            lock (order) order.Add(delta.Key.Collection);
            if (delta.Key.Collection == "orders") await gate.WaitAsync(CancellationToken.None);
            return new SchemaCommitResult(SchemaCommitOutcome.Applied, 1, null);
        };
        var coordinator = NewCoordinator(repository);
        try
        {
            var ordersA = SchemaLearningAdmissionPolicy.Evaluate(CompleteFindResult("shop", "orders", "{\"a\":1}"),
                SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 1, 0), SchemaLearningPolicy.Default).Envelope!;
            var ordersB = SchemaLearningAdmissionPolicy.Evaluate(CompleteFindResult("shop", "orders", "{\"a\":2}"),
                SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 2, 0), SchemaLearningPolicy.Default).Envelope!;
            var customers = SchemaLearningAdmissionPolicy.Evaluate(CompleteFindResult("shop", "customers", "{\"a\":3}"),
                SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 3, 0), SchemaLearningPolicy.Default).Envelope!;

            coordinator.TryEnqueue(ordersA);
            coordinator.TryEnqueue(ordersB);
            coordinator.TryEnqueue(customers);

            // The single sequential worker must reach "orders" (and block on the gate) before it can possibly
            // touch "customers"; releasing the gate afterwards lets both queued "orders" batches finish first.
            Assert.That(SpinWait.SpinUntil(() => order.Count == 1, TimeSpan.FromSeconds(2)), Is.True);
            Assert.That(order, Is.EqualTo(JustOrders));
            gate.Release(2); // Unblocks ordersA now, and ordersB whenever the sequential worker reaches it next.
            Assert.That(SpinWait.SpinUntil(() => order.Count == 3, TimeSpan.FromSeconds(2)), Is.True);
            Assert.That(order, Is.EqualTo(OrdersThenOrdersThenCustomers),
                "As duas entregas de \"orders\" são processadas em sequência antes de qualquer envelope de \"customers\".");
        }
        finally
        {
            gate.Release(10);
            coordinator.StopAsync().GetAwaiter().GetResult();
        }
    }

    [Test]
    public async Task WorkspaceTabViewModelExecutesNormallyWithoutSchemaLearningConfigured()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        context.Mongo.Handler = (name, _) => name switch
        {
            "QueryAsync" => Task.FromResult(new QueryPage(["{\"_id\":1,\"nome\":\"Ana\"}"], TimeSpan.Zero, false)),
            _ => throw new NotSupportedException(name)
        };
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, Database = "loja", Collection = "clientes", Mode = "Consulta JSON", IsConnected = true, Text = "{}" };

        await tab.ExecuteCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(tab.Errors, Is.Empty);
            Assert.That(tab.Documents, Has.Count.EqualTo(1));
            Assert.That(tab.Status, Is.EqualTo("Concluído"));
        });
    }
}
