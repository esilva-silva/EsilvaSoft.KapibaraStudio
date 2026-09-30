using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class MongoshProcessOrchestrationTests
{
    [Test]
    public async Task CapturedEnvironmentAndDatabaseReachRunnerWithoutCredentialsInSource()
    {
        using var repository = new MemoryWorkspaceRepository();
        var vault = repository.LoadEnvironments();
        vault.Environments[0].Values["captured"] = "vault-before";
        repository.SaveEnvironments(vault);
        var host = new HostSnapshot(new() { ["PASSWORD"] = "secret-canary", ["processValue"] = "before" });
        var profile = ConnectionProfile.Create("fixture", "mongodb://user:${PASSWORD}@localhost", "original");
        var runner = new MemoryRunner();
        runner.Execute = (request, _) =>
        {
            host.Values["processValue"] = "after";
            vault.Environments[0].Values["captured"] = "vault-after";
            repository.SaveEnvironments(vault);
            return Task.FromResult(new MongoshProcessResult(0,
                "__SLOPDATAADMIN_RESULT__{\"exact\":{\"$numberLong\":\"9223372036854775807\"}}\nmessage secret-canary", ""));
        };
        var service = new MongoshScriptExecutionService(runner, repository, hostEnvironment: host);

        var result = await service.ExecuteAsync(profile, "slop.results.emit(input);", "{}", "captured-database");

        Assert.Multiple(() =>
        {
            Assert.That(runner.Requests.Single().ConnectionString, Is.EqualTo("mongodb://user:secret-canary@localhost"));
            Assert.That(runner.Requests.Single().ScriptSource, Does.Contain("captured-database").And.Not.Contain("secret-canary"));
            Assert.That(runner.Requests.Single().EnvironmentValues["processValue"], Is.EqualTo("before"));
            Assert.That(runner.Requests.Single().EnvironmentValues["captured"], Is.EqualTo("vault-before"));
            Assert.That(result.Results.Single(), Does.Contain("9223372036854775807"));
            Assert.That(result.StandardOutput, Does.Contain("[redigido]").And.Not.Contain("secret-canary"));
        });
    }

    [Test]
    public void ReadOnlyAndInvalidInputDoNotAcquireProcessResources()
    {
        var runner = new MemoryRunner();
        var service = new MongoshScriptExecutionService(runner);
        var profile = ConnectionProfile.Create("fixture", "mongodb://localhost", isReadOnly: true);
        Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(profile, "1"));
        Assert.CatchAsync<ArgumentException>(() => service.ExecuteAsync(profile with { IsReadOnly = false }, "1", "{ invalid }"));
        Assert.That(runner.Requests, Is.Empty);
    }

    [Test]
    public async Task RunnerFailureDoesNotPreventTheNextExecution()
    {
        var runner = new MemoryRunner { Execute = (_, _) => throw new InvalidOperationException("Runner indisponível.") };
        var service = new MongoshScriptExecutionService(runner);
        var profile = ConnectionProfile.Create("fixture", "mongodb://localhost");
        Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(profile, "1"));
        runner.Execute = (_, _) => Task.FromResult(new MongoshProcessResult(0, "__SLOPDATAADMIN_RESULT__{\"ok\":true}", ""));

        var result = await service.ExecuteAsync(profile, "2");

        Assert.That(result.Results.Single(), Is.EqualTo("{\"ok\":true}"));
        Assert.That(runner.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ConcurrentScriptsCancelOnlyTheirOwnExecution()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new MemoryRunner
        {
            Execute = async (request, token) =>
            {
                if (request.ScriptSource.Contains("FIRST_EXECUTION", StringComparison.Ordinal))
                {
                    firstStarted.SetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
                else await releaseSecond.Task.WaitAsync(token);
                return new(0, "__SLOPDATAADMIN_RESULT__{\"ok\":true}", "");
            }
        };
        var service = new MongoshScriptExecutionService(runner);
        var profile = ConnectionProfile.Create("fixture", "mongodb://localhost");
        using var cancellation = new CancellationTokenSource();
        var first = service.ExecuteAsync(profile, "// FIRST_EXECUTION", cancellationToken: cancellation.Token);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.ExecuteAsync(profile, "// SECOND_EXECUTION");
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await first);
        releaseSecond.SetResult();
        var completed = await second;

        Assert.That(completed.Results, Has.Count.EqualTo(1));
        Assert.That(runner.Requests, Has.Count.EqualTo(2));
    }

    private sealed class HostSnapshot(Dictionary<string, string> values) : IHostEnvironmentSnapshot
    {
        public Dictionary<string, string> Values { get; } = values;
        public IReadOnlyDictionary<string, string> Capture() => Values;
    }

    private sealed class MemoryRunner : IMongoshScriptProcessRunner
    {
        private readonly object _gate = new();
        public List<MongoshProcessRequest> Requests { get; } = [];
        public Func<MongoshProcessRequest, CancellationToken, Task<MongoshProcessResult>> Execute { get; set; } =
            (_, _) => Task.FromResult(new MongoshProcessResult(0, "", ""));

        public Task<MongoshProcessResult> RunAsync(MongoshProcessRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate) Requests.Add(request);
            return Execute(request, cancellationToken);
        }
    }
}
