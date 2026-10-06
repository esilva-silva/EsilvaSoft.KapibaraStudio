using System.Collections.Concurrent;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class MongoAgentReadCredentialTests
{
    private static readonly string[] Tools = ["get_indexes", "mongo_find", "sample_documents", "mongo_find_one",
        "get_document", "mongo_count", "mongo_distinct", "mongo_explain"];

    private static ConnectionProfile Profile(string user = "reader") =>
        ConnectionProfile.Create("Synthetic", $"mongodb://{user}@synthetic.invalid/app") with
        {
            SecretReference = new SecretReference(Guid.NewGuid()), SourceGenerationId = Guid.NewGuid()
        };

    [TestCaseSource(nameof(Tools))]
    public void StoredCredentialIsResolvedBeforeRequestingTheCapturedClient(string tool)
    {
        var profile = Profile();
        var store = new CredentialStore((_, _) => Task.FromResult(
            SecretStoreResults.Success("mongodb://reader:synthetic-password-canary@synthetic.invalid/app")));
        var pool = new RecordingPool();

        Assert.ThrowsAsync<ClientRequestedException>(() => InvokeAsync(tool, profile, pool, store));

        Assert.Multiple(() =>
        {
            Assert.That(store.Reads, Is.EqualTo(new[] { profile.SecretReference }));
            Assert.That(pool.Requests, Is.EqualTo(new[] { ("reader", "synthetic.invalid") }));
            Assert.That(profile.ConnectionString, Does.Not.Contain("password-canary"));
        });
    }

    [TestCase("denied")]
    [TestCase("missing")]
    [TestCase("exception")]
    [TestCase("mismatch")]
    public void FailedCredentialReadNeverRequestsClientAndNextOperationCanRecover(string failure)
    {
        var profile = Profile();
        var healthy = false;
        var store = new CredentialStore((_, _) => healthy
            ? Task.FromResult(SecretStoreResults.Success("mongodb://reader:synthetic-password-canary@synthetic.invalid/app"))
            : failure switch
            {
                "denied" => Task.FromResult(SecretStoreResults.Failed<string>(SecretStoreFailureCode.Denied)),
                "missing" => Task.FromResult(SecretStoreResults.Failed<string>(SecretStoreFailureCode.NotFound)),
                "exception" => throw new IOException("private-credential-canary"),
                _ => Task.FromResult(SecretStoreResults.Success("mongodb://reader:synthetic-password-canary@other.invalid/app"))
            });
        var pool = new RecordingPool();
        foreach (var tool in Tools)
        {
            var error = Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(tool, profile, pool, store));
            Assert.That(error!.ToString(), Does.Not.Contain("canary").And.Not.Contain(".invalid"));
        }
        Assert.That(pool.Requests, Is.Empty);
        Assert.That(store.Reads, Has.Count.EqualTo(Tools.Length), "Uma leitura por chamada, sem retry.");
        healthy = true;
        foreach (var tool in Tools)
            Assert.ThrowsAsync<ClientRequestedException>(() => InvokeAsync(tool, profile, pool, store));
        Assert.That(pool.Requests, Has.Count.EqualTo(Tools.Length));
    }

    [TestCase("get_indexes", true)]
    [TestCase("get_indexes", false)]
    [TestCase("mongo_find", true)]
    [TestCase("mongo_find", false)]
    [TestCase("mongo_explain", true)]
    [TestCase("mongo_explain", false)]
    public async Task CancelledCredentialReadDoesNotCancelOrRedirectAConcurrentConnection(string tool, bool honorsToken)
    {
        var first = Profile("first");
        var second = Profile("second");
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSecret = new TaskCompletionSource<SecretStoreResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSecret = new TaskCompletionSource<SecretStoreResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new CredentialStore((reference, token) =>
        {
            if (reference == first.SecretReference)
            {
                firstStarted.SetResult();
                return honorsToken ? firstSecret.Task.WaitAsync(token) : firstSecret.Task;
            }
            secondStarted.SetResult();
            return secondSecret.Task.WaitAsync(token);
        });
        var pool = new RecordingPool();
        using var cancelled = new CancellationTokenSource();
        var firstOperation = InvokeAsync(tool, first, pool, store, cancelled.Token);
        var secondOperation = InvokeAsync(tool, second, pool, store);
        await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));

        await cancelled.CancelAsync();
        firstSecret.SetResult(SecretStoreResults.Success("mongodb://first:synthetic-password-canary@synthetic.invalid/app"));
        Assert.That(async () => await firstOperation, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(secondOperation.IsCompleted, Is.False);
        secondSecret.SetResult(SecretStoreResults.Success("mongodb://second:synthetic-password-canary@synthetic.invalid/app"));
        Assert.ThrowsAsync<ClientRequestedException>(async () => await secondOperation);

        Assert.Multiple(() =>
        {
            Assert.That(pool.Requests, Is.EqualTo(new[] { ("second", "synthetic.invalid") }));
            Assert.That(store.Reads, Is.EquivalentTo(new[] { first.SecretReference, second.SecretReference }));
        });
    }

    private static Task InvokeAsync(string tool, ConnectionProfile profile, RecordingPool pool,
        ISecretStore store, CancellationToken token = default)
    {
        var find = new MongoAgentFindSource(new SessionConnectionSecretStore(), null, pool, store);
        return tool switch
        {
            "get_indexes" => new MongoAgentIndexSource(new SessionConnectionSecretStore(), null, pool, store)
                .GetIndexesAsync(profile, "app", "items", TimeSpan.FromSeconds(10), token),
            "mongo_explain" => new MongoAgentExplainSource(new SessionConnectionSecretStore(), null, pool, store)
                .ExplainAsync(profile, new("app", "items", "{}", null, null, 1, 0, 1_000), token),
            "mongo_count" => find.CountAsync(profile, new("app", "items", "{}", 1_000), token),
            "mongo_distinct" => find.DistinctAsync(profile, new("app", "items", "value", "{}", 10, 1_000), token),
            "get_document" => find.FindByIdAsync(profile, new("app", "items", "{\"$numberLong\":\"7\"}", 1_000), token),
            _ => find.FindAsync(profile, new("app", "items", "{}", null, null, 1, 0, 1_000), token)
        };
    }

    private sealed class ClientRequestedException : Exception;
    private sealed class RecordingPool : IMongoClientPool
    {
        public ConcurrentQueue<(string User, string Host)> Requests { get; } = new();
        public IMongoClient GetClient(MongoClientSettings settings)
        {
            Requests.Enqueue((settings.Credential.Username, settings.Server.Host));
            throw new ClientRequestedException(); // Stops before any driver/network operation.
        }
    }

    private sealed class CredentialStore(Func<SecretReference, CancellationToken, Task<SecretStoreResult<string>>> read) : ISecretStore
    {
        public ConcurrentQueue<SecretReference> Reads { get; } = new();
        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            Reads.Enqueue(reference);
            return read(reference, cancellationToken);
        }
        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.Available));
        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret,
            CancellationToken cancellationToken = default) => throw new AssertionException("Não pode gravar credencial.");
        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference,
            CancellationToken cancellationToken = default) => throw new AssertionException("Não pode apagar credencial.");
    }
}
