using System.Diagnostics;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.ClaudeCode;

/// <summary>
/// Regressões dos achados da revisão independente P7-CL3-06 (H3, M2, M3, M4, L1, L2, L8 e a lacuna do neto órfão),
/// com o CLI falso. Cada teste falhava com o código anterior à correção.
/// </summary>
[TestFixture]
[CancelAfter(120_000)]
public sealed class ClaudeCodeReviewFixTests
{
    private static async Task<ClaudeCodeAgentSession> SessionAsync(ClaudeCodeAgentProvider provider, string? model = null,
        string? workingDirectory = null) =>
        (ClaudeCodeAgentSession)await provider.CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id, model, workingDirectory), CancellationToken.None);

    private static string ValueAfter(string[] argv, string flag) => argv[Array.IndexOf(argv, flag) + 1];

    // H3 ----------------------------------------------------------------------------------------------------------

    [Test]
    public async Task WorkspaceSourceIsReadOnceSynchronouslyBeforeTheFirstAwait()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        var workspace = Directory.CreateDirectory(Path.Combine(fixture.Root, "projeto")).FullName;
        fixture.Save(workspace);
        var returned = false;
        var calls = 0;
        var readAfterReturn = false;
        var callerThread = Environment.CurrentManagedThreadId;
        var readOnOtherThread = false;
        var provider = fixture.Provider(fixture.Options(workspace: () =>
        {
            calls++;
            readAfterReturn |= Volatile.Read(ref returned);
            readOnOtherThread |= Environment.CurrentManagedThreadId != callerThread;
            return workspace;
        }));

        var creation = provider.CreateSessionAsync(new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        Volatile.Write(ref returned, true);
        await using var session = (ClaudeCodeAgentSession)await creation;

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(readAfterReturn, Is.False, "Lida depois de um await (thread arbitrária).");
            Assert.That(readOnOtherThread, Is.False, "Lida fora da thread do chamador.");
            Assert.That(session.Profile.WorkingDirectory, Is.EqualTo(workspace));
        });
    }

    [Test]
    public async Task CallerCapturedWorkingDirectoryWinsAndTheOptionSourceIsNotRead()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        var captured = Directory.CreateDirectory(Path.Combine(fixture.Root, "capturada")).FullName;
        fixture.Save(captured);
        var calls = 0;
        var provider = fixture.Provider(fixture.Options(workspace: () => { calls++; return null; }));

        await using var session = await SessionAsync(provider, workingDirectory: captured);
        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.Zero);
            Assert.That(session.Profile.WorkingDirectoryKind, Is.EqualTo(ClaudeCodeWorkingDirectoryKind.Workspace));
            Assert.That(fixture.TurnInvocations(captured), Has.Count.EqualTo(1));
            Assert.That(ClaudeCodeFixture.Text(events), Is.EqualTo("ok"));
        });
    }

    [Test]
    public async Task StateQueriesNeitherReadTheWorkspaceNorCreateDirectories()
    {
        using var fixture = new ClaudeCodeFixture();
        var missingDedicated = Path.Combine(fixture.Root, "ainda-nao-existe", "empty");
        var calls = 0;
        var baseOptions = fixture.Options(workspace: () => { calls++; return fixture.Root; });
        var provider = fixture.Provider(new ClaudeCodeAgentProviderOptions
        {
            ExecutablePath = baseOptions.ExecutablePath, DedicatedWorkingDirectory = missingDedicated, AppDataDirectory = baseOptions.AppDataDirectory,
            DatabasePath = baseOptions.DatabasePath, WorkspaceDirectory = baseOptions.WorkspaceDirectory, DefaultModel = "haiku",
            ProbeTimeout = baseOptions.ProbeTimeout, IsEnvironmentVariableSet = baseOptions.IsEnvironmentVariableSet,
        });

        var auth = await provider.GetAuthenticationStatusAsync();
        var status = await provider.GetStatusAsync(CancellationToken.None);
        var installation = await provider.DetectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(installation.State, Is.EqualTo(ClaudeCodeInstallationState.Found));
            Assert.That(auth.Kind, Is.EqualTo(ClaudeCodeAuthKind.NotLoggedIn), "Sem cenário no cwd temporário o falso diz não logado.");
            Assert.That(status.IsAvailable, Is.False);
            Assert.That(calls, Is.Zero, "Consulta de estado não lê a pasta de workspace.");
            Assert.That(Directory.Exists(Path.GetDirectoryName(missingDedicated)), Is.False, "Consulta de estado não cria pastas.");
        });
    }

    [Test]
    public async Task ProfileFailuresBecomeTypedUnavailabilityInsteadOfRawExceptions()
    {
        using var fixture = new ClaudeCodeFixture();
        var baseOptions = fixture.Options();
        // Pasta dedicada dentro do diretório de dados: recusada pela política (área protegida).
        var provider = fixture.Provider(new ClaudeCodeAgentProviderOptions
        {
            ExecutablePath = baseOptions.ExecutablePath, DedicatedWorkingDirectory = Path.Combine(fixture.AppData, "cwd"),
            AppDataDirectory = baseOptions.AppDataDirectory, DatabasePath = baseOptions.DatabasePath, DefaultModel = "haiku",
            ProbeTimeout = baseOptions.ProbeTimeout, IsEnvironmentVariableSet = baseOptions.IsEnvironmentVariableSet,
        });

        var auth = await provider.GetAuthenticationStatusAsync();
        var exception = Assert.CatchAsync(() => provider.CreateSessionAsync(new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(auth.Kind, Is.EqualTo(ClaudeCodeAuthKind.Unreadable));
            Assert.That(exception, Is.TypeOf<ClaudeCodeUnavailableException>());
            Assert.That(((ClaudeCodeUnavailableException)exception!).Reason, Is.EqualTo(ClaudeCodeUnavailableReason.InvalidConfiguration));
        });
    }

    // Acréscimo H3: prévia pública da pasta efetiva ----------------------------------------------------------------

    [Test]
    public void PreviewReportsTheEffectiveDirectoryAndRejectionReasonWithoutSideEffects()
    {
        using var fixture = new ClaudeCodeFixture();
        var missingDedicated = Path.Combine(fixture.Root, "dedicada-ainda-nao-criada", "empty");
        var baseOptions = fixture.Options();
        var provider = fixture.Provider(new ClaudeCodeAgentProviderOptions
        {
            ExecutablePath = baseOptions.ExecutablePath, DedicatedWorkingDirectory = missingDedicated,
            AppDataDirectory = baseOptions.AppDataDirectory, DatabasePath = baseOptions.DatabasePath, DefaultModel = "haiku",
            IsEnvironmentVariableSet = baseOptions.IsEnvironmentVariableSet,
        });
        var project = Directory.CreateDirectory(Path.Combine(fixture.Root, "projeto")).FullName;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var accepted = provider.PreviewWorkingDirectory(project);
        var none = provider.PreviewWorkingDirectory(null);
        var relative = provider.PreviewWorkingDirectory("relativo");
        var missing = provider.PreviewWorkingDirectory(Path.Combine(fixture.Root, "nao-existe"));
        var root = provider.PreviewWorkingDirectory(Path.GetPathRoot(fixture.Root));
        var profile = provider.PreviewWorkingDirectory(home);
        var data = provider.PreviewWorkingDirectory(fixture.AppData);
        var dataParent = provider.PreviewWorkingDirectory(Path.GetDirectoryName(fixture.AppData));

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.EqualTo(new ClaudeCodeWorkingDirectoryPreview(project, ClaudeCodeWorkingDirectoryKind.Workspace,
                ClaudeCodeWorkspaceRejection.None)));
            Assert.That(accepted.ReadsRequireApproval, Is.False);
            Assert.That(none.Rejection, Is.EqualTo(ClaudeCodeWorkspaceRejection.NotProvided));
            Assert.That(none.Directory, Is.EqualTo(missingDedicated));
            Assert.That(none.ReadsRequireApproval, Is.True);
            Assert.That(relative.Rejection, Is.EqualTo(ClaudeCodeWorkspaceRejection.InvalidPath));
            Assert.That(missing.Rejection, Is.EqualTo(ClaudeCodeWorkspaceRejection.NotFound));
            Assert.That(root.Rejection, Is.EqualTo(ClaudeCodeWorkspaceRejection.VolumeRoot));
            Assert.That(profile.Rejection, Is.EqualTo(ClaudeCodeWorkspaceRejection.UserProfile));
            Assert.That((data.Rejection, data.ProtectedArea, data.Relation), Is.EqualTo((ClaudeCodeWorkspaceRejection.ProtectedArea,
                ClaudeCodeProtectedArea.AppData, ClaudeCodeProtectedRelation.SameOrInside)));
            Assert.That((dataParent.Rejection, dataParent.ProtectedArea, dataParent.Relation), Is.EqualTo((ClaudeCodeWorkspaceRejection.ProtectedArea,
                ClaudeCodeProtectedArea.AppData, ClaudeCodeProtectedRelation.Contains)));
            Assert.That(new[] { none, relative, missing, root, profile, data, dataParent }.Select(static p => p.Kind),
                Is.All.EqualTo(ClaudeCodeWorkingDirectoryKind.Dedicated));
            Assert.That(new[] { none, data }.All(static p => p.DedicatedDirectoryUsable), Is.True);
            Assert.That(Directory.Exists(Path.GetDirectoryName(missingDedicated)), Is.False, "A prévia não cria a pasta dedicada.");
            Assert.That(fixture.Invocations(), Is.Empty, "A prévia não inicia processo.");
        });
    }

    [Test]
    public async Task PreviewMatchesTheDirectoryTheSessionActuallyUses()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        var project = Directory.CreateDirectory(Path.Combine(fixture.Root, "projeto")).FullName;
        // A sessão consulta o auth status no próprio cwd escolhido: o cenário do falso precisa estar lá também.
        fixture.Save(project);
        var provider = fixture.Provider();

        foreach (var candidate in new[] { project, fixture.AppData, null })
        {
            var preview = provider.PreviewWorkingDirectory(candidate);
            await using var session = await SessionAsync(provider, workingDirectory: candidate);
            Assert.That((session.Profile.WorkingDirectory, session.Profile.WorkingDirectoryKind),
                Is.EqualTo((preview.Directory, preview.Kind)), candidate ?? "<nula>");
        }
    }

    [Test]
    public void PreviewFlagsAProtectedDedicatedDirectoryAsUnusable()
    {
        using var fixture = new ClaudeCodeFixture();
        var baseOptions = fixture.Options();
        var provider = fixture.Provider(new ClaudeCodeAgentProviderOptions
        {
            ExecutablePath = baseOptions.ExecutablePath, DedicatedWorkingDirectory = Path.Combine(fixture.AppData, "cwd"),
            AppDataDirectory = baseOptions.AppDataDirectory, DatabasePath = baseOptions.DatabasePath, DefaultModel = "haiku",
        });

        var preview = provider.PreviewWorkingDirectory(null);

        Assert.Multiple(() =>
        {
            Assert.That(preview.DedicatedDirectoryUsable, Is.False);
            Assert.That(Directory.Exists(Path.Combine(fixture.AppData, "cwd")), Is.False);
        });
    }

    // M2 / L1 / L2 --------------------------------------------------------------------------------------------------

    [TestCase("subagent-frame.jsonl")]
    [TestCase("result-without-init.jsonl")]
    [TestCase("result-without-session.jsonl")]
    public async Task ProtocolViolationsAbortTheTurn(string fixtureName)
    {
        using var fixture = new ClaudeCodeFixture().Turn(fixtureName);
        await using var session = await SessionAsync(fixture.Provider());

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.ProtocolViolation));
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
        });
    }

    // M3 ------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task PromptSentWithoutValidatedInitRotatesTheCliSessionId()
    {
        using var fixture = new ClaudeCodeFixture().Turn("exit-before-init.jsonl");
        await using var session = await SessionAsync(fixture.Provider());

        var failed = await ClaudeCodeFixture.RunAsync(session);
        fixture.Turn("basic-turn1.jsonl");
        var next = await ClaudeCodeFixture.RunAsync(session);

        var turns = fixture.TurnInvocations();
        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(failed), Is.Not.Null);
            Assert.That(turns[1], Does.Contain("--session-id").And.Not.Contain("--resume"));
            Assert.That(ValueAfter(turns[1], "--session-id"), Is.Not.EqualTo(ValueAfter(turns[0], "--session-id")),
                "O ID que já saiu com o prompt não é reutilizado com --session-id.");
            Assert.That(ClaudeCodeFixture.Text(next), Is.EqualTo("ok"));
        });
    }

    // M4 ------------------------------------------------------------------------------------------------------------

    [TestCase("ANTHROPIC_MODEL")]
    [TestCase("ANTHROPIC_DEFAULT_SONNET_MODEL")]
    [TestCase("ANTHROPIC_DEFAULT_OPUS_MODEL")]
    [TestCase("ANTHROPIC_DEFAULT_HAIKU_MODEL")]
    [TestCase("ANTHROPIC_SMALL_FAST_MODEL")]
    [TestCase("ANTHROPIC_CUSTOM_HEADERS")]
    [TestCase("HTTP_PROXY")]
    [TestCase("HTTPS_PROXY")]
    [TestCase("http_proxy")]
    [TestCase("https_proxy")]
    [TestCase("NODE_EXTRA_CA_CERTS")]
    [TestCase("NODE_TLS_REJECT_UNAUTHORIZED")]
    [TestCase("CLAUDE_CONFIG_DIR")]
    public async Task VariablesThatRedirectModelTransportOrConfigurationBlockByName(string variable)
    {
        using var fixture = new ClaudeCodeFixture();
        var provider = fixture.Provider(fixture.Options(environment: name => name == variable));

        var status = await provider.GetStatusAsync(CancellationToken.None);
        var auth = await provider.GetAuthenticationStatusAsync();

        Assert.Multiple(() =>
        {
            Assert.That(status.UnavailableCode, Is.EqualTo(nameof(ClaudeCodeUnavailableReason.BlockedEnvironment)));
            Assert.That(auth.EnvironmentVariableName, Is.EqualTo(variable));
            Assert.That(fixture.Invocations().Where(static argv => argv.Contains("auth") || argv.Contains("-p")), Is.Empty);
        });
    }

    [Test]
    public void EmptyValueCountsAsPresent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeAuthStatus.IsPresent(string.Empty), Is.True);
            Assert.That(ClaudeCodeAuthStatus.IsPresent(null), Is.False);
        });
    }

    [TestCase("opus", false)]
    [TestCase("sonnet", false)]
    [TestCase("claude-sonnet-4-5", false)]
    [TestCase("haiku", true)]
    [TestCase("claude-haiku-4-5-20251001", true)]
    public async Task InitModelMustMatchTheRequestedModel(string requested, bool accepted)
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        var baseOptions = fixture.Options();
        var provider = fixture.Provider(new ClaudeCodeAgentProviderOptions
        {
            ExecutablePath = baseOptions.ExecutablePath, DedicatedWorkingDirectory = baseOptions.DedicatedWorkingDirectory,
            AppDataDirectory = baseOptions.AppDataDirectory, DatabasePath = baseOptions.DatabasePath,
            AllowedModelIds = ["haiku", "opus", "sonnet", "claude-sonnet-4-5", "claude-haiku-4-5-20251001"], DefaultModel = requested,
            ProbeTimeout = baseOptions.ProbeTimeout, IsEnvironmentVariableSet = baseOptions.IsEnvironmentVariableSet,
        });
        await using var session = await SessionAsync(provider);

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.That(ClaudeCodeFixture.Error(events), accepted ? Is.Null : Is.EqualTo(ClaudeCodeErrorCodes.InitMismatch),
            "O init da fixture reporta claude-haiku-4-5-20251001.");
    }

    // Neto órfão -----------------------------------------------------------------------------------------------------

    [Test]
    public async Task OrphanedGrandchildIsKilledByTheJobObjectOrProcessGroup()
    {
        using var fixture = new ClaudeCodeFixture().Turn("cancel-with-grandchild.jsonl");
        await using var session = await SessionAsync(fixture.Provider());
        var request = ClaudeCodeFixture.Request("Rode algo longo");
        var run = Task.Run(async () =>
        {
            await foreach (var _ in session.RunTurnAsync(request, CancellationToken.None))
            {
            }
        });
        var grandchild = await WaitForLoggedPidAsync(fixture, "grandchild");

        await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
        await run.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.That(WaitUntilGone(grandchild), Is.True,
            "O neto órfão (pai já encerrado) só é alcançado pelo Job Object/grupo, não pela árvore de pais.");
    }

    [Test]
    public async Task KillingTheTreeByParentLinksDoesNotReachAnOrphanedGrandchild()
    {
        // Controle negativo: sem Job/grupo, Kill(entireProcessTree) não acha o neto cujo pai já saiu.
        using var fixture = new ClaudeCodeFixture().Turn("cancel-with-grandchild.jsonl");
        var info = new ProcessStartInfo(ClaudeCodeFixture.FakeExecutable)
        {
            WorkingDirectory = fixture.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-p", "--session-id", Guid.NewGuid().ToString("D"), "--tools", "Read,Glob,Grep" })
        {
            info.ArgumentList.Add(argument);
        }

        using var parent = Process.Start(info)!;
        _ = parent.StandardOutput.ReadToEndAsync();
        await parent.StandardInput.WriteLineAsync("{\"type\":\"user\"}");
        await parent.StandardInput.FlushAsync();
        var grandchild = await WaitForLoggedPidAsync(fixture, "grandchild");
        try
        {
            parent.Kill(entireProcessTree: true);
            await parent.WaitForExitAsync();
            Assert.That(WaitUntilGone(grandchild, TimeSpan.FromSeconds(2)), Is.False);
        }
        finally
        {
            try
            {
                Process.GetProcessById(grandchild).Kill();
            }
            catch (ArgumentException)
            {
            }
        }
    }

    // L8 ------------------------------------------------------------------------------------------------------------

    [Test]
    public void FakeCliIsNeverPublishedOrReferencedByTheProduct()
    {
        var root = RepositoryRoot();
        var fake = File.ReadAllText(Path.Combine(root, "tests", "EsilvaSoft.KapibaraStudio.FakeClaudeCode", "EsilvaSoft.KapibaraStudio.FakeClaudeCode.csproj"));
        var productProjects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);

        Assert.Multiple(() =>
        {
            Assert.That(fake, Does.Contain("<IsPublishable>false</IsPublishable>").And.Contain("<IsPackable>false</IsPackable>"));
            foreach (var project in productProjects)
            {
                Assert.That(File.ReadAllText(project), Does.Not.Contain("FakeClaudeCode"), project);
            }
        });
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EsilvaSoft.KapibaraStudio.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    private static async Task<int> WaitForLoggedPidAsync(ClaudeCodeFixture fixture, string kind)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (fixture.Log().FirstOrDefault(e => e.GetProperty("event").GetString() == kind) is { ValueKind: JsonValueKind.Object } entry)
            {
                return entry.GetProperty("pid").GetInt32();
            }

            await Task.Delay(50);
        }

        Assert.Fail("O CLI falso não registrou " + kind + ".");
        return 0;
    }

    private static bool WaitUntilGone(int pid, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }
}
