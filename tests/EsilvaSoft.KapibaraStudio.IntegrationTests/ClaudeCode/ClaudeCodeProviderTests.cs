using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Anthropic;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.ClaudeCode;

/// <summary>
/// Detecção, classificação da autenticação, descritor, composição e configuração do modo Claude (assinatura), com o
/// CLI falso. Os casos de <c>auth status</c> reproduzem os campos observados no spike P7-CL0-01 (auth-status.json).
/// </summary>
[TestFixture]
[CancelAfter(60_000)]
public sealed class ClaudeCodeProviderTests
{
    private static readonly string[] ExpectedDenyRules =
    [
        "Read(~/AppData/Local/EsilvaSoft/KapibaraStudio/**)", "Read(~/AppData/Local/EsilvaSoft/KapibaraStudio/workspace.db)",
        "Read(~/.claude/**)", "Read(~/.ssh/**)",
    ];

    private static readonly string[] ReadTools = ["Read", "Glob", "Grep"];
    private static readonly string[] LoginPair = ["auth", "login"];
    private static readonly string[] LogoutPair = ["auth", "logout"];

    // Formas observadas no spike (valores de conta substituídos por canários).
    private const string ApiKeyInEnvironment =
        """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","apiKeySource":"ANTHROPIC_API_KEY","subscriptionType":null,"email":"x@example.invalid","orgId":"o","orgName":"n"}""";
    private const string OAuthToken = """{"loggedIn":true,"authMethod":"oauth_token","apiProvider":"firstParty","analyticsDisabled":false}""";
    private const string ApiKeyHelper = """{"loggedIn":true,"authMethod":"api_key_helper","apiProvider":"firstParty","apiKeySource":"apiKeyHelper"}""";
    private const string Bedrock = """{"loggedIn":true,"authMethod":"third_party","apiProvider":"bedrock"}""";
    private const string NoSubscription = """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":null}""";
    private const string LoggedOut = """{"loggedIn":false,"authMethod":"none","apiProvider":"firstParty"}""";

    [TestCase(ApiKeyInEnvironment, ClaudeCodeAuthKind.ApiKey)]
    [TestCase(OAuthToken, ClaudeCodeAuthKind.EnvironmentToken)]
    [TestCase(ApiKeyHelper, ClaudeCodeAuthKind.ApiKeyHelper)]
    [TestCase(Bedrock, ClaudeCodeAuthKind.CloudProvider)]
    [TestCase(NoSubscription, ClaudeCodeAuthKind.UnsupportedMethod)]
    [TestCase(LoggedOut, ClaudeCodeAuthKind.NotLoggedIn)]
    [TestCase("Login method: Claude Pro", ClaudeCodeAuthKind.Unreadable)]
    [TestCase("""{"authMethod":"claude.ai"}""", ClaudeCodeAuthKind.Unreadable)]
    [TestCase("", ClaudeCodeAuthKind.Unreadable)]
    public void AuthStatusIsClassifiedByAllowlistedFieldsNeverByAuthMethodAlone(string output, ClaudeCodeAuthKind expected)
    {
        var status = ClaudeCodeAuthStatus.Parse(output);

        Assert.Multiple(() =>
        {
            Assert.That(status.Kind, Is.EqualTo(expected));
            Assert.That(status.IsSubscription, Is.False);
            Assert.That(status.ToString(), Does.Not.Contain("@").And.Not.Contain("orgName"));
        });
    }

    [Test]
    public void SubscriptionRequiresClaudeAiWithoutApiKeySourceAndWithSubscriptionType()
    {
        var status = ClaudeCodeAuthStatus.Parse(ClaudeCodeFixture.SubscriptionStatus);

        Assert.Multiple(() =>
        {
            Assert.That(status.Kind, Is.EqualTo(ClaudeCodeAuthKind.Subscription));
            Assert.That(status.SubscriptionType, Is.EqualTo("pro"));
            Assert.That(status.ToString(), Does.Not.Contain(ClaudeCodeFixture.EmailCanary).And.Not.Contain(ClaudeCodeFixture.OrgCanary));
        });
    }

    [TestCase(ClaudeCodeAuthKind.Subscription, true, AgentProviderAuthState.Configured, null)]
    [TestCase(ClaudeCodeAuthKind.NotLoggedIn, false, AgentProviderAuthState.NotConfigured, "NotLoggedIn")]
    [TestCase(ClaudeCodeAuthKind.ApiKey, false, AgentProviderAuthState.Invalid, "NonSubscriptionAuthentication")]
    [TestCase(ClaudeCodeAuthKind.EnvironmentToken, false, AgentProviderAuthState.Invalid, "NonSubscriptionAuthentication")]
    [TestCase(ClaudeCodeAuthKind.Unreadable, false, AgentProviderAuthState.Unknown, "AuthStatusUnreadable")]
    public async Task StatusReflectsTheEffectiveMethodWithSafeCodes(ClaudeCodeAuthKind kind, bool available, AgentProviderAuthState auth, string? code)
    {
        using var fixture = new ClaudeCodeFixture();
        fixture.AuthStatus(kind switch
        {
            ClaudeCodeAuthKind.Subscription => ClaudeCodeFixture.SubscriptionStatus,
            ClaudeCodeAuthKind.NotLoggedIn => LoggedOut,
            ClaudeCodeAuthKind.ApiKey => ApiKeyInEnvironment,
            ClaudeCodeAuthKind.EnvironmentToken => OAuthToken,
            _ => "\"texto\"",
        }, exitCode: kind == ClaudeCodeAuthKind.NotLoggedIn ? 1 : 0);

        var status = await fixture.Provider().GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsAvailable, Is.EqualTo(available));
            Assert.That(status.AuthState, Is.EqualTo(auth));
            Assert.That(status.UnavailableCode, Is.EqualTo(code));
            Assert.That(status.Capabilities.UsesNetwork, Is.True);
            Assert.That(status.Capabilities.Chat, Is.EqualTo(available));
            Assert.That(status.Capabilities.ToolCalling, Is.False);
        });
    }

    [Test]
    public async Task BlockingEnvironmentVariableIsReportedByNameWithoutRunningTheCli()
    {
        using var fixture = new ClaudeCodeFixture();
        var provider = fixture.Provider(fixture.Options(environment: static name => name == "CLAUDECODE"));

        var status = await provider.GetStatusAsync(CancellationToken.None);
        var auth = await provider.GetAuthenticationStatusAsync();

        Assert.Multiple(() =>
        {
            Assert.That(status.UnavailableCode, Is.EqualTo(nameof(ClaudeCodeUnavailableReason.BlockedEnvironment)));
            Assert.That(auth.Kind, Is.EqualTo(ClaudeCodeAuthKind.BlockedEnvironment));
            Assert.That(auth.EnvironmentVariableName, Is.EqualTo("CLAUDECODE"));
            Assert.That(fixture.Invocations().Where(static argv => argv.Contains("auth")), Is.Empty);
        });
    }

    [TestCase("2.1.267 (Claude Code)", ClaudeCodeInstallationState.VersionTooLow)]
    [TestCase("1.0.0", ClaudeCodeInstallationState.VersionTooLow)]
    [TestCase("versão desconhecida", ClaudeCodeInstallationState.VersionUnreadable)]
    [TestCase("2.1", ClaudeCodeInstallationState.VersionUnreadable)]
    [TestCase("2.1.268 (Claude Code)", ClaudeCodeInstallationState.Found)]
    [TestCase("2.2.0", ClaudeCodeInstallationState.Found)]
    public async Task VersionBelowTheMinimumOrUnreadableIsRejected(string version, ClaudeCodeInstallationState expected)
    {
        using var fixture = new ClaudeCodeFixture().Version(version);

        var installation = await fixture.Provider().DetectAsync();

        Assert.That(installation.State, Is.EqualTo(expected));
    }

    [Test]
    public async Task SlowVersionProbeTimesOutAndIsKilled()
    {
        using var fixture = new ClaudeCodeFixture().Version("2.1.268", delayMs: 20_000);
        var options = fixture.Options();
        var provider = fixture.Provider(new ClaudeCodeAgentProviderOptions
        {
            ExecutablePath = options.ExecutablePath, DedicatedWorkingDirectory = options.DedicatedWorkingDirectory,
            AppDataDirectory = options.AppDataDirectory, DatabasePath = options.DatabasePath,
            IsEnvironmentVariableSet = options.IsEnvironmentVariableSet, ProbeTimeout = TimeSpan.FromSeconds(2),
        });

        var installation = await provider.DetectAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.That(installation.State, Is.EqualTo(ClaudeCodeInstallationState.ProbeTimedOut));
    }

    [Test]
    public void LocatorFindsNothingWithoutCandidatesAndRejectsShimsScriptsAndNonNativeFiles()
    {
        using var fixture = new ClaudeCodeFixture();
        var bin = Directory.CreateDirectory(Path.Combine(fixture.Root, "bin")).FullName;
        var home = Directory.CreateDirectory(Path.Combine(fixture.Root, "home")).FullName;
        var empty = new ClaudeCodeExecutableLocator(bin, home, null);
        Assert.That(empty.Locate(null).State, Is.EqualTo(ClaudeCodeExecutableState.NotFound));

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(bin, "claude.cmd"), "@echo off\r\nnode cli.js %*");
            File.WriteAllText(Path.Combine(bin, "claude.ps1"), "node cli.js $args");
        }
        else
        {
            File.WriteAllText(Path.Combine(bin, "claude"), "#!/usr/bin/env node\nrequire('./cli.js')");
        }

        var textExe = Path.Combine(fixture.Root, "fake.exe");
        File.WriteAllText(textExe, "not a binary");
        Assert.Multiple(() =>
        {
            Assert.That(new ClaudeCodeExecutableLocator(bin, home, null).Locate(null),
                Is.EqualTo(((string?)null, ClaudeCodeExecutableState.UnsupportedExecutable)));
            Assert.That(ClaudeCodeExecutableLocator.Validate(Path.Combine(bin, OperatingSystem.IsWindows() ? "claude.cmd" : "claude")), Is.Null);
            Assert.That(ClaudeCodeExecutableLocator.Validate(textExe), Is.Null, "Extensão .exe sem cabeçalho nativo.");
            Assert.That(ClaudeCodeExecutableLocator.Validate("claude.exe"), Is.Null, "Caminho relativo nunca é aceito.");
            Assert.That(ClaudeCodeExecutableLocator.Validate(ClaudeCodeFixture.FakeExecutable), Is.Not.Null);
        });
    }

    [Test]
    public void LocatorFindsNativeExecutableInPathAndInKnownLocations()
    {
        using var fixture = new ClaudeCodeFixture();
        var name = OperatingSystem.IsWindows() ? "claude.exe" : "claude";
        var header = OperatingSystem.IsWindows() ? "MZ\u0090\0"u8.ToArray() : [0x7F, (byte)'E', (byte)'L', (byte)'F'];
        var home = Directory.CreateDirectory(Path.Combine(fixture.Root, "home")).FullName;
        var localBin = Directory.CreateDirectory(Path.Combine(home, ".local", "bin")).FullName;
        File.WriteAllBytes(Path.Combine(localBin, name), header);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Path.Combine(localBin, name), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var pathDir = Directory.CreateDirectory(Path.Combine(fixture.Root, "path")).FullName;

        var fromHome = new ClaudeCodeExecutableLocator(pathDir, home, null).Locate(null);
        File.WriteAllBytes(Path.Combine(pathDir, name), header);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Path.Combine(pathDir, name), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var fromPath = new ClaudeCodeExecutableLocator(pathDir, home, null).Locate(null);

        Assert.Multiple(() =>
        {
            Assert.That(fromHome.Path, Is.EqualTo(Path.Combine(localBin, name)));
            Assert.That(fromPath.Path, Is.EqualTo(Path.Combine(pathDir, name)), "PATH tem precedência sobre locais conhecidos.");
        });

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Directory.CreateDirectory(Path.Combine(fixture.Root, "localappdata")).FullName;
            var package = Directory.CreateDirectory(Path.Combine(localAppData, "Microsoft", "WinGet", "Packages",
                "Anthropic.ClaudeCode_Microsoft.Winget.Source_8wekyb3d8bbwe")).FullName;
            File.WriteAllBytes(Path.Combine(package, name), header);
            var fromWinGet = new ClaudeCodeExecutableLocator(null, Path.Combine(fixture.Root, "nohome"), localAppData).Locate(null);
            Assert.That(fromWinGet.Path, Is.EqualTo(Path.Combine(package, name)));
        }
    }

    [Test]
    public void LinuxLocatorSkipsNonExecutableElfAndFindsTheNextNativeCandidate()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Requer permissões Unix reais.");
        using var fixture = new ClaudeCodeFixture();
        var pathDir = Directory.CreateDirectory(Path.Combine(fixture.Root, "path")).FullName;
        var localBin = Directory.CreateDirectory(Path.Combine(fixture.Root, "home", ".local", "bin")).FullName;
        var rejected = Path.Combine(pathDir, "claude");
        var accepted = Path.Combine(localBin, "claude");
        byte[] header = [0x7F, (byte)'E', (byte)'L', (byte)'F'];
        File.WriteAllBytes(rejected, header);
        File.WriteAllBytes(accepted, header);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(rejected, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(accepted, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        Assert.That(ClaudeCodeExecutableLocator.Validate(rejected), Is.Null);
        Assert.That(new ClaudeCodeExecutableLocator(pathDir, Path.Combine(fixture.Root, "home"), null).Locate(null).Path,
            Is.EqualTo(accepted));
        File.Delete(accepted);
        Assert.That(new ClaudeCodeExecutableLocator(pathDir, Path.Combine(fixture.Root, "home"), null).Locate(null).State,
            Is.EqualTo(ClaudeCodeExecutableState.UnsupportedExecutable));
    }

    [Test]
    public async Task MissingConfiguredExecutableMakesTheProviderUnavailableWithoutThrowing()
    {
        using var fixture = new ClaudeCodeFixture();
        var provider = fixture.Provider(fixture.Options(executable: Path.Combine(fixture.Root, "nao-existe", "claude.exe")));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsAvailable, Is.False);
            Assert.That(status.UnavailableCode, Is.EqualTo(nameof(ClaudeCodeUnavailableReason.ExecutableNotFound)));
            Assert.ThrowsAsync<ClaudeCodeUnavailableException>(() =>
                provider.CreateSessionAsync(new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None));
        });
    }

    [Test]
    public void DescriptorIsSeparateFromTheApiModeAndDeclaresOnlyProvenCapabilities()
    {
        var provider = new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions(), new LocalClaudeCodeSystem());
        var descriptor = provider.Describe();

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.ProviderId, Is.EqualTo("claude-code").And.Not.EqualTo(ClaudeAgentProvider.Id));
            Assert.That(descriptor.DisplayName, Is.EqualTo("Claude (assinatura)").And.Not.EqualTo(ClaudeAgentProvider.DisplayName));
            Assert.That(descriptor.AuthenticationMethods, Is.EqualTo(new[] { AgentAuthenticationMethod.OfficialCliDelegated }));
            Assert.That(descriptor.RequiresApiKey, Is.False, "Sem fallback para API Key: o modo não aceita chave.");
            Assert.That(((IAgentProvider)provider).IsLocal, Is.False);
            Assert.That(descriptor.Capabilities.Chat && descriptor.Capabilities.Streaming && descriptor.Capabilities.Sessions, Is.True);
            Assert.That(descriptor.Capabilities.ToolCalling, Is.False, "Leituras nativas não passam pelo registry; sem tools do produto.");
            Assert.That(descriptor.Capabilities.UsesNetwork, Is.True);
            Assert.That(descriptor.Capabilities.Evidence, Is.EqualTo(AgentCapabilityEvidence.AutomatedContract));
        });
    }

    [Test]
    public async Task CompositionIsLazyAndDistinctFromTheApiProvider()
    {
        using var fixture = new ClaudeCodeFixture();
        var services = new ServiceCollection();
        services.AddKapibaraStudioClaudeCodeAgentProvider(fixture.Options());
        await using var container = services.BuildServiceProvider();

        var providers = container.GetServices<IAgentProvider>().ToArray();
        _ = providers.Single().Describe();

        Assert.Multiple(() =>
        {
            Assert.That(providers.Single(), Is.InstanceOf<ClaudeCodeAgentProvider>());
            Assert.That(fixture.Invocations(), Is.Empty, "Registrar/resolver/descrever não inicia processo.");
            Assert.Throws<InvalidOperationException>(() => services.AddKapibaraStudioClaudeCodeAgentProvider());
        });
    }

    [Test]
    public void DedicatedDirectorySettingsAskForEveryReadAndDenyProtectedPaths()
    {
        var home = OperatingSystem.IsWindows() ? @"C:\Users\pessoa" : "/home/pessoa";
        var appData = Path.Combine(home, "AppData", "Local", "EsilvaSoft", "KapibaraStudio");
        var deny = ClaudeCodeCommandLine.BuildDenyRules(appData, Path.Combine(appData, "workspace.db"), home);

        using var dedicated = JsonDocument.Parse(ClaudeCodeCommandLine.BuildSettingsJson(ClaudeCodeWorkingDirectoryKind.Dedicated, deny));
        using var workspace = JsonDocument.Parse(ClaudeCodeCommandLine.BuildSettingsJson(ClaudeCodeWorkingDirectoryKind.Workspace, deny));
        var permissions = dedicated.RootElement.GetProperty("permissions");

        Assert.Multiple(() =>
        {
            Assert.That(deny, Is.EqualTo(ExpectedDenyRules));
            Assert.That(dedicated.RootElement.GetProperty("disableAllHooks").GetBoolean(), Is.True);
            Assert.That(permissions.GetProperty("ask").EnumerateArray().Select(static e => e.GetString()),
                Is.EqualTo(ClaudeCodeAgentProviderOptions.NativeToolAllowlist),
                "O perfil dedicado pede confirmação para toda ferramenta nativa permitida pelo adapter; --tools continua limitado por turno.");
            Assert.That(permissions.GetProperty("deny").GetArrayLength(), Is.EqualTo(4));
            Assert.That(workspace.RootElement.GetProperty("permissions").TryGetProperty("ask", out _), Is.False);
            Assert.That(workspace.RootElement.GetProperty("permissions").GetProperty("deny").GetArrayLength(), Is.EqualTo(4));
            Assert.That(dedicated.RootElement.TryGetProperty("allow", out _) || permissions.TryGetProperty("allow", out _), Is.False);
        });
    }

    [Test]
    public void ProtectedPathOutsideTheProfileUsesTheDocumentedAbsoluteForm()
    {
        var home = OperatingSystem.IsWindows() ? @"C:\Users\pessoa" : "/home/pessoa";
        var appData = OperatingSystem.IsWindows() ? @"D:\Dados\KapibaraStudio" : "/srv/dados/KapibaraStudio";

        var deny = ClaudeCodeCommandLine.BuildDenyRules(appData, Path.Combine(appData, "workspace.db"), home);

        Assert.That(deny[0], Is.EqualTo(OperatingSystem.IsWindows() ? "Read(//d/Dados/KapibaraStudio/**)" : "Read(//srv/dados/KapibaraStudio/**)"));
    }

    [Test]
    public async Task UserWorkspaceBecomesTheWorkingDirectoryWithoutAskRules()
    {
        using var fixture = new ClaudeCodeFixture();
        var workspace = Directory.CreateDirectory(Path.Combine(fixture.Root, "meu-projeto")).FullName;
        fixture.Turn("basic-turn1.jsonl").Save(workspace);
        var provider = fixture.Provider(fixture.Options(workspace: () => workspace));

        await using var session = (ClaudeCodeAgentSession)await provider.CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        var events = await ClaudeCodeFixture.RunAsync(session);
        var turn = fixture.TurnInvocations(workspace).Single();

        Assert.Multiple(() =>
        {
            Assert.That(session.Profile.WorkingDirectoryKind, Is.EqualTo(ClaudeCodeWorkingDirectoryKind.Workspace));
            Assert.That(session.Profile.WorkingDirectory, Is.EqualTo(workspace));
            Assert.That(ClaudeCodeFixture.Text(events), Is.EqualTo("ok"));
            Assert.That(turn[Array.IndexOf(turn, "--settings") + 1], Does.Not.Contain("\"ask\""));
        });
    }

    [Test]
    public void UnsafeWorkspaceChoicesFallBackToTheDedicatedDirectory()
    {
        using var fixture = new ClaudeCodeFixture();
        var home = Directory.CreateDirectory(Path.Combine(fixture.Root, "home")).FullName;
        var appData = Directory.CreateDirectory(Path.Combine(home, "AppData", "KapibaraStudio")).FullName;
        var sshDir = Directory.CreateDirectory(Path.Combine(home, ".ssh")).FullName;
        string[] protectedPaths = [appData, Path.Combine(appData, "workspace.db"), Path.Combine(home, ".claude"), sshDir];

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeWorkspacePolicy.TryAcceptWorkspace(home, home, protectedPaths, new LocalClaudeCodeSystem()), Is.Null, "Perfil inteiro.");
            Assert.That(ClaudeCodeWorkspacePolicy.TryAcceptWorkspace(Path.GetPathRoot(fixture.Root), home, protectedPaths, new LocalClaudeCodeSystem()), Is.Null, "Raiz do volume.");
            Assert.That(ClaudeCodeWorkspacePolicy.TryAcceptWorkspace(appData, home, protectedPaths, new LocalClaudeCodeSystem()), Is.Null, "Diretório de dados.");
            Assert.That(ClaudeCodeWorkspacePolicy.TryAcceptWorkspace(Path.Combine(home, "AppData"), home, protectedPaths, new LocalClaudeCodeSystem()), Is.Null,
                "Pasta que contém o diretório de dados.");
            Assert.That(ClaudeCodeWorkspacePolicy.TryAcceptWorkspace(sshDir, home, protectedPaths, new LocalClaudeCodeSystem()), Is.Null);
            Assert.That(ClaudeCodeWorkspacePolicy.TryAcceptWorkspace("relativo", home, protectedPaths, new LocalClaudeCodeSystem()), Is.Null);
            Assert.That(ClaudeCodeWorkspacePolicy.TryAcceptWorkspace(Path.Combine(home, "nao-existe"), home, protectedPaths, new LocalClaudeCodeSystem()), Is.Null);
            Assert.That(ClaudeCodeWorkspacePolicy.TryAcceptWorkspace(
                Directory.CreateDirectory(Path.Combine(home, "projeto")).FullName, home, protectedPaths, new LocalClaudeCodeSystem()), Is.Not.Null);
        });

        var resolved = ClaudeCodeWorkspacePolicy.Resolve(fixture.Options(), home, fixture.AppData, true, new LocalClaudeCodeSystem());
        Assert.That(resolved, Is.EqualTo((fixture.WorkingDirectory, ClaudeCodeWorkingDirectoryKind.Dedicated)));
    }

    [Test]
    public void LoginAndLogoutOpenTheOfficialCommandInAVisibleWindowWithoutReadingItsOutput()
    {
        var info = ClaudeCodeAccountCommands.CreateStartInfo(ClaudeCodeFixture.FakeExecutable, ClaudeCodeCommandLine.LoginArguments, Path.GetTempPath());

        Assume.That(info, Is.Not.Null, "Linux sem emulador de terminal no PATH.");
        Assert.Multiple(() =>
        {
            Assert.That(info!.ArgumentList, Does.Contain("auth"));
            Assert.That(info.ArgumentList.Skip(info.ArgumentList.Count - 2), Is.EqualTo(LoginPair));
            Assert.That(info.ArgumentList, Does.Not.Contain("--console"), "Sem --console: o login é o da assinatura.");
            Assert.That(info.RedirectStandardOutput || info.RedirectStandardError || info.RedirectStandardInput, Is.False);
            Assert.That(info.CreateNoWindow, Is.False);
            Assert.That(ClaudeCodeCommandLine.LogoutArguments, Is.EqualTo(LogoutPair));
        });
    }

    [TestCase(0, ClaudeCodeAccountCommandState.Completed)]
    [TestCase(1, ClaudeCodeAccountCommandState.CommandFailed)]
    [TestCase(-1, ClaudeCodeAccountCommandState.CommandFailed)]
    public void VisibleAccountCommandReportsProcessExitOutcome(int exitCode, ClaudeCodeAccountCommandState expected)
    {
        Assert.That(ClaudeCodeAccountCommands.ClassifyExitCode(exitCode), Is.EqualTo(expected));
    }

    [Test]
    [Platform("Linux")]
    public void LinuxTerminalSearchSkipsAnInexecutableFirstPathEntry()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Permissões POSIX exigem Linux.");

        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KapibaraStudio.ClaudeTerminalTest"));
        var root = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        var first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        var rejected = Path.Combine(first, "x-terminal-emulator");
        var accepted = Path.Combine(second, "x-terminal-emulator");
        try
        {
            File.WriteAllText(rejected, "synthetic terminal");
            File.SetUnixFileMode(rejected, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllText(accepted, "synthetic terminal");
            File.SetUnixFileMode(accepted, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var selected = ClaudeCodeAccountCommands.FindLinuxTerminal(first + Path.PathSeparator + second);
            Assert.That(selected?.Path, Is.EqualTo(accepted));
        }
        finally
        {
            if (root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal) && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void GlobalLogoutRequiresExplicitConfirmation()
    {
        using var fixture = new ClaudeCodeFixture();

        Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provider().LogoutAsync(userConfirmedGlobalLogout: false));
        Assert.That(fixture.Invocations(), Is.Empty);
    }

    [TestCase("sonnet", true)]
    [TestCase("claude-haiku-4-5-20251001", true)]
    [TestCase("opus[1m]", true)]
    [TestCase("--dangerously-skip-permissions", false)]
    [TestCase("-p", false)]
    [TestCase("sonnet haiku", false)]
    [TestCase("", false)]
    public void ModelIdsCannotInjectFlags(string model, bool accepted) =>
        Assert.That(ClaudeCodeCommandLine.IsSafeModelId(model), Is.EqualTo(accepted));
}
