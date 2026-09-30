using System.Text.Json;
using System.Text.Json.Nodes;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.ClaudeCode;

/// <summary>
/// Ambiente isolado por teste para o CLI falso (<c>tests/EsilvaSoft.KapibaraStudio.FakeClaudeCode</c>, executável nativo
/// chamado <c>claude</c>): pasta dedicada (cwd, com o cenário e o log do falso), diretório de dados e LiteDB falsos.
/// Nenhum teste automatizado usa o Claude Code real, conta, rede ou <c>~/.claude</c>.
/// </summary>
internal sealed class ClaudeCodeFixture : IDisposable
{
    /// <summary>Canários: campos de conta do auth status e texto de stderr que nunca podem chegar a eventos/erros.</summary>
    public const string EmailCanary = "canario-conta@example.invalid";
    public const string OrgCanary = "org-canario-7f3a";
    public const string StderrCanary = "sk-ant-canario-stderr";

    private readonly JsonObject _scenario = new();

    public ClaudeCodeFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "slop-claude-code-tests", Guid.NewGuid().ToString("N"));
        WorkingDirectory = Path.Combine(Root, "cwd");
        AppData = Path.Combine(Root, "appdata", "KapibaraStudio");
        Directory.CreateDirectory(WorkingDirectory);
        Directory.CreateDirectory(AppData);
        AuthStatus(SubscriptionStatus);
    }

    public static string SubscriptionStatus =>
        $$"""{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","email":"{{EmailCanary}}","orgId":"{{OrgCanary}}","orgName":"Org Canario","subscriptionType":"pro","analyticsDisabled":false,"projectsDirectory":"<HOME>/.claude/projects","configDirectory":"<HOME>/.claude"}""";

    public string Root { get; }

    public string WorkingDirectory { get; }

    public string AppData { get; }

    public static string FakeExecutable
    {
        get
        {
            var testsBin = AppContext.BaseDirectory;
            var candidate = testsBin.Replace("EsilvaSoft.KapibaraStudio.IntegrationTests", "EsilvaSoft.KapibaraStudio.FakeClaudeCode",
                StringComparison.Ordinal);
            var path = Path.Combine(candidate, OperatingSystem.IsWindows() ? "claude.exe" : "claude");
            if (!File.Exists(path))
            {
                Assert.Fail("CLI falso não compilado: execute dotnet build EsilvaSoft.KapibaraStudio.slnx. Esperado em " + path);
            }

            return path;
        }
    }

    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "ClaudeCode", "Fixtures", name);

    public ClaudeCodeFixture AuthStatus(string json, int exitCode = 0)
    {
        _scenario["authStatus"] = JsonNode.Parse(json);
        _scenario["authExitCode"] = exitCode;
        return Save();
    }

    public ClaudeCodeFixture Version(string version, int delayMs = 0)
    {
        _scenario["version"] = version;
        _scenario["versionDelayMs"] = delayMs;
        return Save();
    }

    public ClaudeCodeFixture Turn(string fixture, string? resumeFixture = null)
    {
        _scenario["turnFixture"] = FixturePath(fixture);
        if (resumeFixture is not null)
        {
            _scenario["resumeFixture"] = FixturePath(resumeFixture);
        }

        return Save();
    }

    public ClaudeCodeFixture AuthDelay(int delayMs)
    {
        _scenario["authDelayMs"] = delayMs;
        return Save();
    }

    public ClaudeCodeFixture ResumeMissing(bool missing = true)
    {
        _scenario["resumeMissing"] = missing;
        return Save();
    }

    /// <summary>Tools que o servidor MCP falso "lista" (sem prefixo) quando conectado.</summary>
    public ClaudeCodeFixture McpTools(params string[] tools)
    {
        _scenario["mcpTools"] = new JsonArray([.. tools.Select(static tool => (JsonNode)JsonValue.Create(tool)!)]);
        return Save();
    }

    /// <summary>Status reportado para cada servidor de <c>--mcp-config</c> (padrão <c>connected</c>).</summary>
    public ClaudeCodeFixture McpStatus(string status)
    {
        _scenario["mcpStatus"] = status;
        return Save();
    }

    public ClaudeCodeFixture ExtraInitTools(params string[] tools)
    {
        _scenario["extraInitTools"] = new JsonArray([.. tools.Select(static tool => (JsonNode)JsonValue.Create(tool)!)]);
        return Save();
    }

    public ClaudeCodeFixture AuthStatusAfterTurn(string json)
    {
        _scenario["authStatusAfterTurn"] = JsonNode.Parse(json);
        return Save();
    }

    public ClaudeCodeFixture OmitInitTools(params string[] tools)
    {
        _scenario["omitInitTools"] = new JsonArray([.. tools.Select(static tool => (JsonNode)JsonValue.Create(tool)!)]);
        return Save();
    }

    public ClaudeCodeFixture ExtraMcpServer(string name, string status)
    {
        _scenario["extraMcpServers"] = new JsonArray(new JsonObject { ["name"] = name, ["status"] = status });
        return Save();
    }

    public ClaudeCodeFixture Save(string? directory = null)
    {
        File.WriteAllText(Path.Combine(directory ?? WorkingDirectory, "fake-claude.json"), _scenario.ToJsonString());
        return this;
    }

    public ClaudeCodeAgentProviderOptions Options(
        Func<string, bool>? environment = null, TimeSpan? turnDuration = null, Func<string?>? workspace = null,
        string? executable = null, string? debugLogDirectory = null) => new()
    {
        ExecutablePath = executable ?? FakeExecutable,
        // As fixtures do spike foram gravadas com haiku; o init é validado contra o modelo pedido (M4).
        DefaultModel = "haiku",
        DedicatedWorkingDirectory = WorkingDirectory,
        AppDataDirectory = AppData,
        DatabasePath = Path.Combine(AppData, "workspace.db"),
        WorkspaceDirectory = workspace,
        ProbeTimeout = TimeSpan.FromSeconds(15),
        MaxTurnDuration = turnDuration ?? TimeSpan.FromSeconds(60),
        IsEnvironmentVariableSet = environment ?? (static _ => false),
        DebugLogDirectory = debugLogDirectory,
    };

    public ClaudeCodeAgentProvider Provider(ClaudeCodeAgentProviderOptions? options = null) => new(options ?? Options(), new LocalClaudeCodeSystem());

    /// <summary>Registros do CLI falso (argv, stdin, filhos) no cwd indicado.</summary>
    public IReadOnlyList<JsonElement> Log(string? directory = null)
    {
        var path = Path.Combine(directory ?? WorkingDirectory, "fake-claude.log.jsonl");
        if (!File.Exists(path))
        {
            return [];
        }

        string[] lines;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        return [.. lines.Select(static line => JsonDocument.Parse(line).RootElement.Clone())];
    }

    public IReadOnlyList<string[]> Invocations(string? directory = null) =>
        [.. Log(directory).Where(static e => e.GetProperty("event").GetString() == "start")
            .Select(static e => e.GetProperty("argv").EnumerateArray().Select(static a => a.GetString()!).ToArray())];

    public IReadOnlyList<string[]> TurnInvocations(string? directory = null) =>
        [.. Invocations(directory).Where(static argv => argv.Contains("-p"))];

    /// <summary>Prompt de sistema curto, sem segredos, como o do <c>AgentSystemPromptBuilder</c>.</summary>
    public const string SystemPrompt = "Você é o agente de teste do KapibaraStudio. Modo: Agente. Descubra via tools.";

    /// <summary>
    /// Plano equivalente ao comportamento anterior ao ADR-056 (Read/Glob/Grep, sem tools do produto nem confirmação), para
    /// os testes de protocolo/processo que não dependem do plano.
    /// </summary>
    public static AgentTurnPlan LegacyPlan { get; } = new(AgentOperationMode.Agent, ["Read", "Glob", "Grep"], [], [], [],
        AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None);

    public static AgentTurnRequest Request(string message = "Responda apenas: ok", AgentTurnPlan? plan = null,
        AgentProviderPermissions? permissions = null, Guid? conversationId = null) =>
        new(AgentTurnId.New(), message, "tab-1", 1)
        {
            Plan = plan ?? LegacyPlan,
            SystemPrompt = SystemPrompt,
            Permissions = permissions,
            ConversationId = conversationId,
        };

    public static Task<List<AgentProviderEvent>> RunAsync(IAgentSession session, string message = "Responda apenas: ok",
        CancellationToken cancellationToken = default) => RunAsync(session, Request(message), cancellationToken);

    public static async Task<List<AgentProviderEvent>> RunAsync(IAgentSession session, AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, cancellationToken))
        {
            events.Add(item);
        }

        return events;
    }

    public static string Text(IEnumerable<AgentProviderEvent> events) =>
        string.Concat(events.Where(static e => e.Kind == AgentEventKind.MessageDelta).Select(static e => e.Text));

    public static string? Error(IEnumerable<AgentProviderEvent> events) =>
        events.SingleOrDefault(static e => e.Kind == AgentEventKind.AgentError)?.Text;

    public void Dispose()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
