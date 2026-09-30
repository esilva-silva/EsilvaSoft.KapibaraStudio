using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
[Category("Unit")]
public sealed class AppSystemBoundaryTests
{
    private static readonly Uri AuthorizationUrl = new("https://auth.openai.com/fixture-login");
    private static readonly AgentCliAccountStatus Status = new(AgentCliInstallState.Installed, null, AgentCliAuthState.Subscription, "plus");
    private static readonly string[] ExpectedLoginSteps = ["login", "browser", "login-complete", "status"];

    [Test]
    public void DebugLogDestinationUsesOnlyTheInjectedResolver()
    {
        var resolver = new LogResolverFake { Directory = SyntheticPaths.Combine("diagnostic-logs") };
        var directory = App.DebugLogDirectoryForDesktop(resolver);
#if DEBUG
        Assert.That(directory, Is.EqualTo(resolver.Directory));
        Assert.That(resolver.Calls, Is.EqualTo(1));
#else
        Assert.That(directory, Is.Null);
        Assert.That(resolver.Calls, Is.Zero);
#endif
    }

    [Test]
    public void CompositionPreservesAnInjectedBrowserInsteadOfCreatingAnAdapter()
    {
        var browser = new BrowserFake();
        var services = new ServiceCollection();
        services.AddSingleton<IExternalUriLauncher>(browser);

        App.AddDesktopAgentServices(services, new LocalWorkspacePaths(SyntheticPaths.Combine("workspace", "workspace.db")));

        Assert.That(services.Single(descriptor => descriptor.ServiceType == typeof(IExternalUriLauncher)).ImplementationInstance,
            Is.SameAs(browser));
        Assert.That(browser.Calls, Is.Zero);
    }

    [TestCase(true, AgentCliCommandOutcome.Completed)]
    [TestCase(false, AgentCliCommandOutcome.StartFailed)]
    public async Task BrowserLoginAwaitsTheCapturedUrlThenChecksTheAccount(bool completed, AgentCliCommandOutcome expected)
    {
        var steps = new List<string>();
        var browser = new BrowserFake { Steps = steps };
        using var cancellation = new CancellationTokenSource();
        var result = await App.SignInCodexWithBrowserAsync(async (open, token) =>
        {
            steps.Add("login");
            await open(AuthorizationUrl, token);
            steps.Add("login-complete");
            return completed;
        }, token =>
        {
            Assert.That(token, Is.EqualTo(cancellation.Token));
            steps.Add("status");
            return Task.FromResult(Status);
        }, browser, cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(browser.Destination, Is.EqualTo(AuthorizationUrl));
            Assert.That(browser.Token, Is.EqualTo(cancellation.Token));
            Assert.That(browser.Calls, Is.EqualTo(1));
            Assert.That(steps, Is.EqualTo(ExpectedLoginSteps));
            Assert.That(result.Outcome, Is.EqualTo(expected));
            Assert.That(result.Status, Is.SameAs(Status));
        });
    }

    [Test]
    public async Task BrowserFailureReportsStartFailedAndRechecksStatusWithoutRetrying()
    {
        var browser = new BrowserFake { Failure = new InvalidOperationException("launch unavailable") };
        var checks = 0;
        var result = await App.SignInCodexWithBrowserAsync(async (open, token) =>
        {
            await open(AuthorizationUrl, token);
            return true;
        }, _ => { checks++; return Task.FromResult(Status); }, browser, CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(AgentCliCommandOutcome.StartFailed));
        Assert.That(result.Status, Is.SameAs(Status));
        Assert.That(browser.Calls, Is.EqualTo(1));
        Assert.That(checks, Is.EqualTo(1));
    }

    [Test]
    public void MissingBrowserFailsBeforeStartingTheLoginFlow()
    {
        var logins = 0;
        var checks = 0;
        Assert.ThrowsAsync<ArgumentNullException>(() => App.SignInCodexWithBrowserAsync((_, _) =>
        {
            logins++;
            return Task.FromResult(true);
        }, _ => { checks++; return Task.FromResult(Status); }, null!, CancellationToken.None));

        Assert.That(logins, Is.Zero);
        Assert.That(checks, Is.Zero);
    }

    [Test]
    public async Task CancellationDuringBrowserLaunchRemainsObservableAndDoesNotRecheckAccount()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new BrowserFake { Pending = pending };
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        var operation = App.SignInCodexWithBrowserAsync(async (open, token) =>
        {
            await open(AuthorizationUrl, token);
            return true;
        }, _ => { checks++; return Task.FromResult(Status); }, browser, cancellation.Token);
        await browser.Started.Task;
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await operation);
        Assert.That(browser.Calls, Is.EqualTo(1));
        Assert.That(checks, Is.Zero);
    }

    private sealed class LogResolverFake : IDiagnosticLogDirectoryResolver
    {
        public string Directory { get; init; } = "logs";
        public int Calls { get; private set; }
        public string Resolve() { Calls++; return Directory; }
    }

    private sealed class BrowserFake : IExternalUriLauncher
    {
        public int Calls { get; private set; }
        public Uri? Destination { get; private set; }
        public CancellationToken Token { get; private set; }
        public List<string>? Steps { get; init; }
        public Exception? Failure { get; init; }
        public TaskCompletionSource? Pending { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Calls++;
            Destination = uri;
            Token = cancellationToken;
            Steps?.Add("browser");
            Started.TrySetResult();
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is { } failure ? Task.FromException(failure)
                : Pending?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;
        }
    }
}
