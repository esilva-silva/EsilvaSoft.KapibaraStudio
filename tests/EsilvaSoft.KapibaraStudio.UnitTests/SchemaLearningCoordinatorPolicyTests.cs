using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class SchemaLearningCoordinatorPolicyTests
{
    [Test]
    public async Task TransientPolicyNeverCallsTheDurableRepository()
    {
        var repository = new FakeLearnedSchemaRepository();
        var coordinator = new SchemaLearningCoordinator(new BackgroundSchemaAnalyzer(TimeProvider.System), repository);
        try
        {
            Assert.That(coordinator.TryEnqueue(Envelope(SchemaLearningPolicy.TransientOnly)), Is.True);
            Assert.That(await WaitUntilAsync(() => coordinator.ProcessedCount == 1), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(repository.ApplyCount, Is.Zero);
                Assert.That(coordinator.NotPersistedCount, Is.EqualTo(1));
                Assert.That(coordinator.FailedCount, Is.Zero);
            });
        }
        finally { await coordinator.StopAsync(); }
    }

    [Test]
    public async Task TypedNotPersistedCommitIsCountedAsFailureInsteadOfProcessed()
    {
        var repository = new FakeLearnedSchemaRepository
        {
            OnApply = (_, _) => Task.FromResult(new SchemaCommitResult(SchemaCommitOutcome.NotPersisted, 0, "armazenamento indisponível"))
        };
        var coordinator = new SchemaLearningCoordinator(new BackgroundSchemaAnalyzer(TimeProvider.System), repository);
        try
        {
            Assert.That(coordinator.TryEnqueue(Envelope(SchemaLearningPolicy.Default)), Is.True);
            Assert.That(await WaitUntilAsync(() => coordinator.FailedCount == 1), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(repository.ApplyCount, Is.EqualTo(1));
                Assert.That(coordinator.ProcessedCount, Is.Zero);
                Assert.That(coordinator.NotPersistedCount, Is.Zero);
            });
        }
        finally { await coordinator.StopAsync(); }
    }

    private static SchemaLearningEnvelope Envelope(SchemaLearningPolicy policy) => SchemaLearningEnvelope.Create(
        LearnedSchemaKey.Create(Guid.Parse("b5854160-7f27-4f48-bf7d-6bf36de98c28"), "shop", "orders"),
        SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 1, 0), policy, "find", ["{\"name\":\"Ana\"}"]);

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return false;
    }
}
