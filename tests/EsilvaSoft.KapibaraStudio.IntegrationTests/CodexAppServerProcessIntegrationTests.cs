using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.CodexAppServer;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Codex;
using NUnit.Framework;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Processes;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.CodexAppServer;

[TestFixture]
[NonParallelizable]
[Category("Integration")]
internal sealed class CodexAppServerJsonRpcTransportTests
{
    private string _fake = null!;
    private string _root = null!;
    private string _cwd = null!;
    private string _home = null!;

    [Test]
    public async Task CorrelatesConcurrentRepliesAndDeliversNotifications()
    {
        await using var transport = Start();
        var ready = await NextAsync(transport);
        Assert.That(ready.Method, Is.EqualTo("fake/ready"));
        Assert.That(ready.Parameters.GetProperty("home").GetString(), Is.EqualTo(_home));
        Assert.That(ready.Parameters.GetProperty("leakedKey").GetBoolean(), Is.False);
        var slow = transport.RequestAsync("test/echo", new { value = 1 });
        var fast = transport.RequestAsync("test/echo", new { value = 2 });
        Assert.That((await fast).GetProperty("value").GetInt32(), Is.EqualTo(2));
        Assert.That((await slow).GetProperty("value").GetInt32(), Is.EqualTo(1));
        await transport.RequestAsync("test/notify");
        Assert.That((await NextAsync(transport)).Method, Is.EqualTo("test/event"));
    }

    [Test]
    public async Task CorrelatesServerInitiatedRequestAndClientResponse()
    {
        await using var transport = Start();
        await NextAsync(transport);
        var pending = transport.RequestAsync("test/serverRequest");
        var request = await NextAsync(transport);
        Assert.That(request.Method, Is.EqualTo("test/approve"));
        Assert.That(request.Id?.GetString(), Is.EqualTo("approval-1"));
        await transport.RespondAsync(request.Id!.Value, new { decision = "decline" });
        Assert.That((await pending).GetProperty("accepted").GetBoolean(), Is.True);
    }

    [Test]
    public async Task TimeoutAndCancellationLeaveTransportUsable()
    {
        await using var transport = Start();
        await NextAsync(transport);
        Assert.ThrowsAsync<TimeoutException>(async () =>
            await transport.RequestAsync("test/hang", timeout: TimeSpan.FromMilliseconds(60)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await transport.RequestAsync("test/hang", cancellationToken: cancellation.Token));
        Assert.That((await transport.RequestAsync("test/echo", new { value = 2 }))
            .GetProperty("value").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public async Task RpcErrorOmitsSensitiveServerText()
    {
        await using var transport = Start();
        await NextAsync(transport);
        var error = Assert.ThrowsAsync<CodexAppServerRpcException>(async () =>
            await transport.RequestAsync("test/error"));
        Assert.That(error!.Code, Is.EqualTo(42));
        Assert.That(error.ToString(), Does.Not.Contain("SECRET_RESPONSE_BODY"));
    }

    [Test]
    public async Task InvalidAndOversizeFramesStopPendingWork()
    {
        await using (var invalid = Start())
        {
            await NextAsync(invalid);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await invalid.RequestAsync("test/invalid"));
            Assert.That(await HasExitedAsync(invalid.ProcessId), Is.True);
        }

        await using (var oversized = Start(new CodexAppServerTransportOptions { MaxFrameBytes = 512 }))
        {
            await NextAsync(oversized);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await oversized.RequestAsync("test/oversize"));
            Assert.That(await HasExitedAsync(oversized.ProcessId), Is.True);
        }
    }

    [Test]
    public async Task StderrAndOutgoingLimitsFailClosedWithoutExposingBody()
    {
        await using (var limited = Start(new CodexAppServerTransportOptions { MaxFrameBytes = 512 }))
        {
            await NextAsync(limited);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await limited.RequestAsync("test/echo", new { value = new string('x', 1024) }));
            Assert.That((await limited.RequestAsync("test/echo", new { value = 2 }))
                .GetProperty("value").GetInt32(), Is.EqualTo(2));
        }

        await using (var stderr = Start(new CodexAppServerTransportOptions { MaxStderrBytes = 512 }))
        {
            await NextAsync(stderr);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await stderr.RequestAsync("test/stderrFlood"));
            Assert.That(await HasExitedAsync(stderr.ProcessId), Is.True);
        }
    }

    [Test]
    public async Task DisposeTerminatesDescendantAndHomeIsPrivate()
    {
        var transport = Start();
        await NextAsync(transport);
        var config = File.ReadAllText(Path.Combine(_home, "config.toml"));
        Assert.Multiple(() =>
        {
            Assert.That(config, Does.Contain("cli_auth_credentials_store = \"keyring\""));
            Assert.That(config, Does.Contain("shell_tool = false"));
            Assert.That(config, Does.Contain("unified_exec = false"));
            Assert.That(config, Does.Contain("web_search = \"disabled\""));
            Assert.That(config, Does.Contain("apps = false"));
        });
        var childId = (await transport.RequestAsync("test/spawnChild")).GetProperty("pid").GetInt32();
        await transport.DisposeAsync();
        Assert.That(await HasExitedAsync(childId), Is.True);
        if (OperatingSystem.IsLinux())
        {
            Assert.That(File.GetUnixFileMode(_home),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
        }
    }

    [Test]
    public async Task AccountFlowUsesOnlyChatGptAndRequiresConfirmedLogout()
    {
        await using var flow = await CodexAppServerAccountFlow.InitializeAsync(Start());
        Assert.That((await flow.GetStatusAsync()).State, Is.EqualTo(CodexSubscriptionState.SignedOut));
        var login = await flow.BeginLoginAsync();
        Assert.That(login.AuthorizationUrl.Host, Is.EqualTo("chatgpt.com"));
        Assert.That(await flow.WaitForLoginAsync(login.LoginId), Is.True);
        var status = await flow.GetStatusAsync();
        Assert.That(status.State, Is.EqualTo(CodexSubscriptionState.Authenticated));
        Assert.That(status.PlanType, Is.EqualTo("pro"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await flow.LogoutAsync(confirmed: false));
        await flow.LogoutAsync(confirmed: true);
        Assert.That((await flow.GetStatusAsync()).State, Is.EqualTo(CodexSubscriptionState.SignedOut));
    }

    [Test]
    public async Task AccountFlowRejectsApiKeyAndUntrustedLoginUrl()
    {
        await using (var keyFlow = await CodexAppServerAccountFlow.InitializeAsync(Start(arguments: ["--api-key-mode"])))
        {
            Assert.That((await keyFlow.GetStatusAsync()).State, Is.EqualTo(CodexSubscriptionState.OtherAuthentication));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await keyFlow.LogoutAsync(confirmed: true));
        }

        await using (var badUrlFlow = await CodexAppServerAccountFlow.InitializeAsync(Start(arguments: ["--bad-url"])))
        {
            var error = Assert.ThrowsAsync<InvalidDataException>(async () => await badUrlFlow.BeginLoginAsync());
            Assert.That(error!.ToString(), Does.Not.Contain("evil.invalid"));
        }
    }

    [Test]
    public async Task AccountFlowCancelsOnlyMatchingLogin()
    {
        await using var flow = await CodexAppServerAccountFlow.InitializeAsync(Start(arguments: ["--hold-login"]));
        var login = await flow.BeginLoginAsync();
        Assert.ThrowsAsync<ArgumentException>(async () => await flow.CancelLoginAsync(Guid.NewGuid()));
        await flow.CancelLoginAsync(login.LoginId);
        Assert.That(await flow.WaitForLoginAsync(login.LoginId), Is.False);
    }

    [OneTimeSetUp]
    public async Task BuildFakeAsync()
    {
        EnsureWindowsAclAvailable();

        var project = FindFakeProject();
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var part in new[] { "build", project, "--no-restore", "--nologo", "--verbosity", "quiet", "-p:UsedAvaloniaProducts=" })
        {
            info.ArgumentList.Add(part);
        }

        using var build = Process.Start(info) ?? throw new InvalidOperationException("Fake App Server build did not start.");
        var stdout = build.StandardOutput.ReadToEndAsync();
        var stderr = build.StandardError.ReadToEndAsync();
        await build.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        Assert.That(build.ExitCode, Is.Zero,
            $"The fake App Server must build before protocol tests. stdout: {await stdout}; stderr: {await stderr}");
        _fake = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Debug", "net10.0",
            OperatingSystem.IsWindows() ? "EsilvaSoft.KapibaraStudio.FakeCodexAppServer.exe" :
                "EsilvaSoft.KapibaraStudio.FakeCodexAppServer");
        Assert.That(File.Exists(_fake), Is.True);
    }

    private static void EnsureWindowsAclAvailable()
    {
        if (!OperatingSystem.IsWindows()) return;

        var probeRoot = Path.Combine(Path.GetTempPath(), "slop-codex-acl-preflight", Guid.NewGuid().ToString("N"));
        try
        {
            var sid = WindowsIdentity.GetCurrent().User ??
                throw new UnauthorizedAccessException("A identidade Windows atual não está disponível.");
            var security = new DirectorySecurity();
            security.SetOwner(sid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                AccessControlType.Allow));

            var directory = Directory.CreateDirectory(probeRoot);
            directory.SetAccessControl(security);

            var currentSecurity = new DirectoryInfo(probeRoot).GetAccessControl(AccessControlSections.Owner |
                AccessControlSections.Access);
            if (!sid.Equals(currentSecurity.GetOwner(typeof(SecurityIdentifier))) ||
                !currentSecurity.AreAccessRulesProtected)
            {
                throw new UnauthorizedAccessException("O host não aplicou a ACL privada do diretório de teste.");
            }

            var hasOwnerGrant = false;
            foreach (FileSystemAccessRule rule in currentSecurity.GetAccessRules(includeExplicit: true,
                         includeInherited: true, typeof(SecurityIdentifier)))
            {
                if (!sid.Equals(rule.IdentityReference) && rule.AccessControlType == AccessControlType.Allow)
                {
                    throw new UnauthorizedAccessException("A ACL de teste permite acesso a outra identidade.");
                }

                hasOwnerGrant |= sid.Equals(rule.IdentityReference) && rule.AccessControlType == AccessControlType.Allow &&
                    (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;
            }

            if (!hasOwnerGrant)
            {
                throw new UnauthorizedAccessException("A ACL de teste não concedeu controle total à identidade atual.");
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            Assert.Ignore($"Testes E2E do Codex ignorados: o host bloqueou a operação ACL exigida pelo CodexAppServerHome.PrepareWindows ({exception.GetType().Name}).");
        }
        catch (System.Security.SecurityException exception)
        {
            Assert.Ignore($"Testes E2E do Codex ignorados: o host bloqueou a operação ACL exigida pelo CodexAppServerHome.PrepareWindows ({exception.GetType().Name}).");
        }
        finally
        {
            if (Directory.Exists(probeRoot))
            {
                try { Directory.Delete(probeRoot, recursive: true); }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
    }

    [SetUp]
    public void PrepareDirectories()
    {
        _root = Path.Combine(Path.GetTempPath(), "slop-codex-transport-tests", Guid.NewGuid().ToString("N"));
        _cwd = Path.Combine(_root, "cwd");
        _home = Path.Combine(_root, "codex-home");
        Directory.CreateDirectory(_cwd);
    }

    [TearDown]
    public void RemoveDirectories()
    {
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "slop-codex-transport-tests"));
        var actual = Path.GetFullPath(_root);
        if (actual.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(actual))
        {
            Directory.Delete(actual, recursive: true);
        }
    }

    private CodexAppServerJsonRpcTransport Start(CodexAppServerTransportOptions? options = null,
        IReadOnlyList<string>? arguments = null) =>
        CodexAppServerJsonRpcTransport.Start(new LocalCodexAppServerProcessLauncher(), _fake, _cwd, _home, options, arguments ?? []);

    private static async Task<CodexAppServerInboundMessage> NextAsync(CodexAppServerJsonRpcTransport transport) =>
        await transport.Messages.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    private static async Task<bool> HasExitedAsync(int processId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }

    private static string FindFakeProject()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "tests", "EsilvaSoft.KapibaraStudio.FakeCodexAppServer",
                "EsilvaSoft.KapibaraStudio.FakeCodexAppServer.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Fake Codex App Server project was not found.");
    }
}

[TestFixture]
[Category("Integration")]
internal sealed class CodexAppServerProcessPolicyTests
{
    private string _root = null!;

    [SetUp]
    public void CreateTestRoot() => _root = Path.Combine(Path.GetTempPath(), "slop-codex-policy-test", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void CleanupTestRoot()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "slop-codex-policy-test"));
        var root = Path.GetFullPath(_root);
        if (root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Test]
    public void EnvironmentPolicyRejectsProviderAndTokenVariables()
    {
        var home = _root;
        var info = new ProcessStartInfo();
        info.Environment["OPENAI_API_KEY"] = "sentinel";
        info.Environment["HTTP_PROXY"] = "http://untrusted.invalid";
        info.Environment["LD_PRELOAD"] = "untrusted.so";
        info.Environment["DOTNET_STARTUP_HOOKS"] = "untrusted.dll";
        CodexAppServerProcess.ConfigureEnvironment(info, home);
        Assert.Multiple(() =>
        {
            Assert.That(CodexAppServerProcess.IsSensitiveOrProviderEnvironmentKey("OPENAI_API_KEY"), Is.True);
            Assert.That(CodexAppServerProcess.IsSensitiveOrProviderEnvironmentKey("CODEX_HOME"), Is.True);
            Assert.That(CodexAppServerProcess.IsSensitiveOrProviderEnvironmentKey("SLOP_TEST_API_KEY"), Is.True);
            Assert.That(CodexAppServerProcess.IsSensitiveOrProviderEnvironmentKey("PATH"), Is.False);
            Assert.That(info.Environment["CODEX_HOME"], Is.EqualTo(home));
            Assert.That(info.Environment.ContainsKey("OPENAI_API_KEY"), Is.False);
            Assert.That(info.Environment.ContainsKey("HTTP_PROXY"), Is.False);
            Assert.That(info.Environment.ContainsKey("LD_PRELOAD"), Is.False);
            Assert.That(info.Environment.ContainsKey("DOTNET_STARTUP_HOOKS"), Is.False);
        });
    }
}
