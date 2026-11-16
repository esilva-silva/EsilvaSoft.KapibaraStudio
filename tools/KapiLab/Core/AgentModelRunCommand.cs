using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>
/// <c>agent run</c>: turnos de agente dirigidos por um modelo LOCAL real (pacote Qwen3 <c>local-agent-qwen3-v1</c>) sobre o host
/// de fixture KapiSim. Mesma composição de <see cref="AgentFixtureCommand"/> (infraestrutura da IDE, registry real, grants
/// transitórios de produção, bancos sintéticos semeados), mas o provider é o <see cref="LocalAgentProvider"/> da IDE sobre o
/// serviço ONNX do laboratório, no lugar do provider roteirizado. Cada tarefa é uma mensagem humana sintética em uma sessão nova;
/// o relatório guarda as chamadas de tool (nome, status, código), a resposta final, raciocínio e tempos, e as checagens de
/// aceite declaradas na tarefa (tools esperadas/proibidas, trechos esperados na resposta). Não grava no MongoDB (tools de escrita
/// não existem no catálogo; propostas ficam no sink sintético) e não lê dados do usuário.
/// </summary>
internal static class AgentModelRunCommand
{
    internal const string InputSchema = "kapilab-agent-task-v1";
    internal const string ReportSchema = "kapilab-agent-run-report-v1";
    internal const int MaximumTasks = 100;

    internal sealed record Input(string Schema, string[] Domains, AgentTask[] Tasks, int MaxToolResultBytes = 6144);
    internal sealed record AgentTask(string Id, string Message, string Database, string Collection,
        string[]? ExpectTools = null, string[]? ForbidTools = null, string[]? ExpectAnswerContains = null,
        int TimeoutSeconds = 180, int? ReasoningBudgetTokens = null, string? ActiveFileText = null, bool AttachActiveFile = true);
    internal sealed record ToolCall(string Tool, string? Status, string? ErrorCode);
    internal sealed record TaskResult(string Id, string Outcome, string? RuntimeErrorCode, ToolCall[] ToolCalls, string Answer,
        int AnswerChars, int? ReasoningTokens, bool? ReasoningTruncated, int Proposals, double DurationMs,
        bool ExpectedToolsCalled, bool ExpectedToolsSucceeded, bool ForbiddenToolsAvoided, bool AnswerContainsExpected, bool AllToolCallsSucceeded,
        bool Passed,
        string[] MissingTools, string[] MissingAnswerParts, FixtureMongoAccess.Command[] MongoCommands)
    {
        /// <summary>Semente de amostragem desta execução; nula quando a rodada não fixou sementes.</summary>
        public int? Seed { get; init; }
        /// <summary>Tempo até o primeiro pedaço de resposta visível (depois do raciocínio); nulo se não houve resposta.</summary>
        public double? FirstAnswerMs { get; init; }
        /// <summary>Blocos de raciocínio concluídos no turno e quantos deles foram cortados pelo orçamento.</summary>
        public int ReasoningBlocks { get; init; }
        public int ReasoningBlocksTruncated { get; init; }
    }
    internal sealed record Summary(int Tasks, int Passed, int Completed, int ToolCalls, int ToolCallsSucceeded,
        double PassRate, double DurationMsMean)
    {
        /// <summary>Média e percentil 95 do tempo até a primeira resposta, sobre as execuções que responderam.</summary>
        public double? FirstAnswerMsMean { get; init; }
        public double? FirstAnswerMsP95 { get; init; }
        /// <summary>Execuções que terminaram por envelope inválido, tool fora do plano ou limite de chamadas.</summary>
        public int InvalidToolCallTurns { get; init; }
        /// <summary>Execuções com ao menos um bloco de raciocínio cortado pelo orçamento.</summary>
        public int ReasoningBudgetExceededTurns { get; init; }
        /// <summary>Taxa de aprovação por semente, na ordem pedida; vazio quando a rodada não fixou sementes.</summary>
        public IReadOnlyList<SeedSummary> BySeed { get; init; } = [];
    }
    internal sealed record SeedSummary(int Seed, int Tasks, int Passed, double PassRate);

    /// <summary>
    /// Opções de medição da rodada. Sementes repetem cada tarefa uma vez por semente; <see cref="Thinking"/> nulo mantém o
    /// padrão do pacote e da tarefa, verdadeiro ou falso forçam o modo; o orçamento, quando informado, vale para todas as tarefas.
    /// </summary>
    internal sealed record RunOptions(IReadOnlyList<int> Seeds, bool? Thinking, int? ThinkBudgetTokens)
    {
        public static RunOptions Default { get; } = new([], null, null);
    }

    internal const int MaximumSeeds = 10;

    /// <summary>Valida as opções de medição sem tocar no pacote; devolve a mensagem de uso quando inválidas.</summary>
    internal static bool TryParseRunOptions(string? seeds, string? thinking, int? thinkBudget, out RunOptions options, out string error)
    {
        options = RunOptions.Default;
        error = "";
        var parsedSeeds = new List<int>();
        foreach (var part in (seeds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seed))
            { error = "--seeds aceita inteiros não negativos separados por vírgula."; return false; }
            if (parsedSeeds.Contains(seed)) { error = "--seeds não aceita sementes repetidas."; return false; }
            parsedSeeds.Add(seed);
        }
        if (seeds is not null && parsedSeeds.Count == 0) { error = "--seeds exige ao menos uma semente."; return false; }
        if (parsedSeeds.Count > MaximumSeeds) { error = $"--seeds aceita até {MaximumSeeds} sementes."; return false; }
        bool? mode;
        switch (thinking ?? "default")
        {
            case "default": mode = null; break;
            case "on": mode = true; break;
            case "off": mode = false; break;
            default: error = "--thinking aceita apenas default, on ou off."; return false;
        }
        if (thinkBudget is < 1 or > 8192) { error = "--think-budget deve estar entre 1 e 8192 tokens."; return false; }
        if (mode == false && thinkBudget is not null) { error = "--think-budget não se aplica com --thinking off."; return false; }
        options = new(parsedSeeds, mode, thinkBudget);
        return true;
    }

    /// <summary>
    /// Escolha de raciocínio enviada ao provider para uma tarefa. Sem opções da rodada, só a tarefa com orçamento próprio pede
    /// raciocínio; caso contrário vale o padrão do pacote. O provider recusa o que o pacote não suporta, e isso aparece no relatório.
    /// </summary>
    internal static AgentReasoningOptions? ReasoningFor(AgentTask task, RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Thinking == false) return new AgentReasoningOptions(false);
        var budget = options.ThinkBudgetTokens ?? task.ReasoningBudgetTokens;
        return options.Thinking == true || budget is not null ? new AgentReasoningOptions(true, budget) : null;
    }

    internal static Summary Summarize(IReadOnlyList<TaskResult> results, RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(options);
        var toolCalls = results.Sum(result => result.ToolCalls.Length);
        var answered = results.Where(result => result.FirstAnswerMs is not null).Select(result => result.FirstAnswerMs!.Value).Order().ToArray();
        return new Summary(results.Count, results.Count(result => result.Passed),
            results.Count(result => result.Outcome == nameof(AgentTurnOutcome.Completed)), toolCalls,
            results.Sum(result => result.ToolCalls.Count(call => call.Status == "Succeeded")),
            results.Count == 0 ? 0 : Math.Round((double)results.Count(result => result.Passed) / results.Count, 4),
            results.Count == 0 ? 0 : Math.Round(results.Average(result => result.DurationMs), 1))
        {
            FirstAnswerMsMean = answered.Length == 0 ? null : Math.Round(answered.Average(), 1),
            // Percentil por posto mais próximo: com poucas execuções é o maior valor observado, sem interpolar.
            FirstAnswerMsP95 = answered.Length == 0 ? null : answered[Math.Min(answered.Length - 1, (int)Math.Ceiling(answered.Length * 0.95) - 1)],
            InvalidToolCallTurns = results.Count(result => result.RuntimeErrorCode == "InvalidToolCallFormat"),
            ReasoningBudgetExceededTurns = results.Count(result => result.ReasoningBlocksTruncated > 0),
            BySeed = [.. options.Seeds.Select(seed =>
            {
                var ofSeed = results.Where(result => result.Seed == seed).ToArray();
                var passed = ofSeed.Count(result => result.Passed);
                return new SeedSummary(seed, ofSeed.Length, passed, ofSeed.Length == 0 ? 0 : Math.Round((double)passed / ofSeed.Length, 4));
            })],
        };
    }
    internal sealed record Report(string Schema, string Package, string Model, string? Provider, string? Backend,
        bool GrantsSynthetic, string FixtureSha256, Summary Summary, IReadOnlyList<TaskResult> Tasks, string IdeCommit, string GenAi)
    {
        /// <summary>Se a rodada pediu sessão de geração por turno (reuso de KV entre os passos); medição de laboratório.</summary>
        public bool SessionMode { get; init; }
        /// <summary>Sementes e modo de raciocínio pedidos para a rodada; "default" mantém o padrão do pacote e da tarefa.</summary>
        public IReadOnlyList<int> Seeds { get; init; } = [];
        public string Thinking { get; init; } = "default";
        public int? ThinkBudgetTokens { get; init; }
    }

    internal static Input ReadInput(string root, string path)
    {
        var input = KapiSimFixture.Read<Input>(path);
        if (input.Schema != InputSchema || input.Tasks is not { Length: > 0 and <= MaximumTasks } ||
            input.Domains is not { Length: >= 1 and <= 6 } || input.MaxToolResultBytes is < 1 or > 6144 ||
            input.Tasks.Select(task => task.Id).Distinct(StringComparer.Ordinal).Count() != input.Tasks.Length)
            throw new InvalidDataException("Contrato kapilab-agent-task-v1 inválido.");
        var tools = AgentProductToolNames.ReadTools.Append(AgentProductToolNames.ProposeFileEdit)
            .Append(AgentProductToolNames.CreateWorkspaceFile).ToHashSet(StringComparer.Ordinal);
        foreach (var task in input.Tasks)
        {
            KapiSimFixture.RequireDatabase(task.Database);
            if (string.IsNullOrWhiteSpace(task.Id) || task.Id.Length > 100 || string.IsNullOrWhiteSpace(task.Message) ||
                task.Message.Length > 4096 || string.IsNullOrWhiteSpace(task.Collection) || task.TimeoutSeconds is < 10 or > 900 ||
                task.ReasoningBudgetTokens is < 64 or > 8192 || task.ActiveFileText is { Length: > 16_384 } ||
                (task.ExpectTools ?? []).Concat(task.ForbidTools ?? []).Any(tool => !tools.Contains(tool)) ||
                (task.ExpectAnswerContains ?? []).Any(part => string.IsNullOrWhiteSpace(part) || part.Length > 256))
                throw new InvalidDataException($"Tarefa fora do contrato: {task.Id}.");
            var sensitive = string.Concat(task.Message, task.ActiveFileText ?? "");
            if (CompletionPrivacy.ContainsSensitiveText(sensitive) || sensitive.Contains("<|", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Tarefa contém texto sensível ou marcador reservado.");
        }
        var domains = KapiSimFixture.ReadDomains(root, input.Domains);
        foreach (var task in input.Tasks)
            if (domains.SingleOrDefault(domain => domain.Database == task.Database) is not { } domain ||
                !domain.Collections.Any(collection => collection.Name == task.Collection))
                throw new InvalidDataException($"Tarefa {task.Id} deve apontar para um domínio/coleção semeado pelo host.");
        return input;
    }

    internal static async Task<int> RunAsync(string package, string tasksPath, string? output, string? workspaceOption,
        string device, bool viaQueue, bool standaloneGpu, int? contextTokens, int? taskTimeoutSeconds = null,
        bool sessionMode = false, string? seeds = null, string? thinking = null, int? thinkBudget = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRunOptions(seeds, thinking, thinkBudget, out var runOptions, out var optionsError))
        {
            Console.Error.WriteLine("error " + optionsError);
            return (int)ExitCode.Usage;
        }
        var root = LabWorkspace.Resolve(workspaceOption);
        if (root is null)
        {
            Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE para selecionar o laboratório.");
            return (int)ExitCode.Usage;
        }
        if (!Cli.ModelRunCommand.TryParseDeviceOption(device, out var acceleration))
        {
            Console.Error.WriteLine("error --device aceita apenas auto, cpu, gpu ou npu.");
            return (int)ExitCode.Usage;
        }
        if (!GpuExecutionCoordination.TryParseMode(viaQueue, standaloneGpu, out var gpuMode))
        {
            Console.Error.WriteLine("error escolha apenas um modo GPU.");
            return (int)ExitCode.Usage;
        }
        var path = Cli.KapiLabCommandLine.ReadArtifactPath(tasksPath, root);
        var input = ReadInput(root, path);
        if (taskTimeoutSeconds is { } timeout)
        {
            if (timeout is < 10 or > 3600)
            {
                Console.Error.WriteLine("error --task-timeout deve estar entre 10 e 3600 segundos.");
                return (int)ExitCode.Usage;
            }
            // Override do laboratório: modelos com raciocínio longo precisam de mais tempo por turno que o fixture declara.
            input = input with { Tasks = input.Tasks.Select(task => task with { TimeoutSeconds = timeout }).ToArray() };
        }
        var outputPath = output is null ? null : LabWorkspace.ResolveOutput(root, output);
        if (outputPath is not null) LabWorkspace.RefuseInputOverwrite(root, outputPath,
            input.Domains.Select(domain => Cli.KapiLabCommandLine.ReadArtifactPath(domain, root)).Append(path).ToArray());

        var packagePath = Path.GetFullPath(package);
        LabWorkspace.RefuseBlindInput(packagePath, root);
        var fileAccess = new KapiLabModelFileAccess();
        var catalog = new LocalModelCatalog(Path.GetDirectoryName(packagePath), fileAccess: fileAccess);
        var validation = await catalog.ValidateAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (validation.Status.State != LocalModelState.Available || validation.Model is not { } model ||
            !model.Capabilities.HasFlag(LocalModelCapabilities.Agent) || !model.Capabilities.HasFlag(LocalModelCapabilities.Tools) ||
            model.PromptFormat != LocalModelPromptFormats.Qwen3ChatMlTools || model.Metadata?.Agent is null)
        {
            Console.Error.WriteLine("error pacote sem agente Qwen3 (capabilities agent/tools, bloco agent, qwen3-chatml-tools-v1).");
            return (int)ExitCode.PackageInvalid;
        }
        var settings = new AutocompleteSettings
        {
            ModelPath = packagePath,
            Acceleration = acceleration,
            ChatEnabled = true,
            ContextTokens = Cli.ModelRunChatCommand.ResolveContextTokens(model, contextTokens),
            AiTimeoutMilliseconds = 60_000,
            // Sessão por turno: mesmo mecanismo do produto, que só a honra em CPU. O laboratório pode medi-la em pacote
            // ainda não homologado; o produto exige a declaração do pacote.
            PrefixCacheMode = sessionMode ? PrefixCacheMode.Reuse : PrefixCacheMode.Disabled,
        }.Validate();

        var uri = KapiSimFixture.RequireUri(Environment.GetEnvironmentVariable("KAPILAB_MONGO_URI") ?? "mongodb://127.0.0.1:27017");
        var domains = KapiSimFixture.ReadDomains(root, input.Domains);
        var temporaryRelativePath = Path.Combine("tmp", "kapi-agent-run-" + Guid.NewGuid().ToString("N"));
        var temporaryRoot = LabWorkspace.ResolveOutput(root, temporaryRelativePath);
        var hash = await KapiSimFixture.SeedAsync(uri, domains, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(temporaryRoot);
        await using var models = KapiLabModelServices.Create(catalog, fileAccess, "agent-run", root, gpuMode,
            allowUnvalidatedPrefixCache: sessionMode);
        Report report;
        try
        {
            report = await ExecuteAsync(input, domains, hash, uri, temporaryRoot, models, model, settings, packagePath, runOptions,
                cancellationToken).ConfigureAwait(false) with
            {
                SessionMode = sessionMode, Seeds = runOptions.Seeds, ThinkBudgetTokens = runOptions.ThinkBudgetTokens,
                Thinking = runOptions.Thinking switch { true => "on", false => "off", _ => "default" },
            };
        }
        catch (LocalModelUnavailableException exception) when (KapiLabModelServices.IsGpuLockUnavailable(exception))
        {
            Console.Error.WriteLine("error outra execução do KapiLab mantém o lock de carga GPU.");
            return (int)ExitCode.GpuBusy;
        }
        finally
        {
            Directory.Delete(LabWorkspace.ResolveOutput(root, temporaryRelativePath), recursive: true);
        }
        var json = JsonSerializer.Serialize(report, KapiSimFixture.JsonOptions);
        if (outputPath is null) Console.Out.WriteLine(json);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false); File.Move(temporary, outputPath, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        Console.Error.WriteLine($"info agent.run tasks={report.Summary.Tasks} passed={report.Summary.Passed} " +
            $"toolCalls={report.Summary.ToolCalls} succeeded={report.Summary.ToolCallsSucceeded}");
        return report.Summary.Passed == report.Summary.Tasks ? (int)ExitCode.Success : 6;
    }

    private static async Task<Report> ExecuteAsync(Input input, KapiSimFixture.Domain[] domains, string hash, string uri,
        string temporaryRoot, LocalAiModelService models, LocalModelDefinition model, AutocompleteSettings settings, string packagePath,
        RunOptions runOptions, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(Path.Combine(temporaryRoot, "workspace.db"),
            new AgentPlatformOptions { ToolExposureStage = AgentToolExposureStage.Metadata,
                InProcessToolExposureStage = AgentToolExposureStage.Metadata });
        var ports = AgentFixturePorts.Register(services);
        using var mongoAccess = new FixtureMongoAccess();
        services.RemoveAll<IMongoClientPool>();
        services.AddSingleton<IMongoClientPool>(mongoAccess);
        var metadataRegistration = services.Single(descriptor => descriptor.ServiceType == typeof(IMongoMetadataSource));
        services.Remove(metadataRegistration);
        services.AddSingleton<IMongoMetadataSource>(serviceProvider => new FixtureMetadataSource(
            (IMongoMetadataSource)ActivatorUtilities.CreateInstance(serviceProvider, metadataRegistration.ImplementationType!),
            domains.Select(domain => domain.Database).ToHashSet(StringComparer.Ordinal), mongoAccess));
        // O provider local da IDE, sobre o modelo do laboratório: nada de provider roteirizado.
        services.RemoveAll<ILocalAiModelService>();
        services.AddSingleton<ILocalAiModelService>(models);
        services.RemoveAll<IAutocompleteService>();
        services.AddSingleton<IAutocompleteService>(new LabAutocompleteSettings(settings));
        services.AddSingleton<ISecretStore, LabSecretStore>();
        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IConnectionProfileRepository>();
        var profile = ConnectionProfile.Create("KapiSim sintético", uri, isReadOnly: true);
        await repository.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        profile = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false)).Single();
        var cache = provider.GetRequiredService<IMetadataCache>();
        cache.Connect(profile);
        foreach (var domain in domains)
            foreach (var collection in domain.Collections)
                await cache.SampleSchemaAsync(profile, domain.Database, collection.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var local = provider.GetRequiredService<LocalAgentProvider>();
        var availability = await local.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        if (!availability.IsAvailable || !availability.Capabilities.ToolCalling)
            throw new InvalidDataException($"Agente local indisponível para o pacote: {availability.Reason}.");

        var results = new List<TaskResult>();
        // Sem sementes, cada tarefa roda uma vez com a aleatoriedade do runtime; com sementes, uma vez por semente.
        var seedRuns = runOptions.Seeds.Count == 0 ? new int?[] { null } : [.. runOptions.Seeds.Select(seed => (int?)seed)];
        foreach (var seed in seedRuns)
        foreach (var task in input.Tasks)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(task.TimeoutSeconds));
            var caseFolder = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(caseFolder);
            var text = task.ActiveFileText ?? $"// tarefa sintética\ndb.{task.Collection}.find({{}});\n";
            await File.WriteAllTextAsync(Path.Combine(caseFolder, "query.js"), text, deadline.Token).ConfigureAwait(false);
            ports.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, caseFolder, Path.Combine(caseFolder, "query.js"),
                "query.js", "kapi-agent-tab", 1, text, profile.Id.ToString("D"), profile.Name, task.Database, task.Collection);
            ports.Approve = true;
            var proposalCount = ports.Proposals.Count;
            var permissions = AgentProviderPermissions.Default(LocalAgentProvider.Id) with
            {
                ConnectionScope = AgentConnectionScope.Selected,
                SelectedConnectionIds = [profile.Id],
                EnabledReadTools = AgentProductToolNames.ReadTools,
                DataSending = new() { InferredSchema = true, MongoDocuments = true },
                EditProposals = new() { OtherWorkspaceFiles = true },
                NativeFileWrite = true,
                MaximumToolCallsPerTurn = model.Metadata!.Agent!.MaxToolCallsPerTurn,
                ConfirmationCategories = AgentConfirmationCategories.None,
                // Consentimento sintético de envio (na IDE, aceito pelo usuário na primeira vez); exigido para anexar o buffer ativo.
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            };
            var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new(true, true, false), false);
            // Como o chip automático da IDE: o buffer ativo vai anexado à mensagem (mesmo resolvedor e redação do produto). Sem ele, o
            // runtime recusa propostas no buffer ativo (ActiveFileNotAttached), e o laboratório ficaria mais restrito que a IDE.
            IReadOnlyList<AgentContextAttachment> attachments = [];
            if (task.AttachActiveFile)
            {
                var resolution = await AgentAttachmentResolver.ResolveAsync([new AgentAttachmentRequest(AgentAttachmentKind.ActiveFile)],
                    ports.Context, permissions, task.Message, deadline.Token,
                    pathProbe: provider.GetService<IAgentWorkspacePathProbe>()).ConfigureAwait(false);
                if (!resolution.Succeeded)
                {
                    Console.Error.WriteLine($"error agent.run task={task.Id} anexo do arquivo ativo recusado: " +
                        string.Join(", ", resolution.Failures.Select(f => f.Error)));
                    throw new InvalidDataException($"Tarefa {task.Id}: anexo do arquivo ativo recusado ({string.Join(", ", resolution.Failures.Select(f => f.Error))}).");
                }
                attachments = resolution.Attachments;
            }
            var session = await runtime.StartSessionAsync(new(LocalAgentProvider.Id), deadline.Token).ConfigureAwait(false);
            var turn = AgentTurnId.New();
            var calls = new List<ToolCall>();
            var pending = new Dictionary<string, int>(StringComparer.Ordinal);
            var answer = new StringBuilder();
            string? runtimeError = null;
            AgentTurnOutcome? outcome = null;
            int? reasoningTokens = null;
            bool? reasoningTruncated = null;
            double? firstAnswerMs = null;
            var reasoningBlocks = 0;
            var reasoningBlocksTruncated = 0;
            var watch = Stopwatch.StartNew();
            FixtureMongoAccess.Command[] mongoCommands;
            mongoAccess.BeginTools();
            try
            {
                var request = new AgentTurnRequest(turn, task.Message, "kapi-agent-tab", 1)
                {
                    Plan = plan, Permissions = permissions, ConversationId = Guid.NewGuid(), WorkspaceContext = ports.Context, Attachments = attachments,
                    Reasoning = ReasoningFor(task, runOptions), SamplingSeed = seed,
                };
                await foreach (var evt in runtime.RunTurnAsync(session, request, deadline.Token).ConfigureAwait(false))
                {
                    if (evt.ErrorCode is not null) runtimeError ??= evt.ErrorCode;
                    if (evt.Outcome is { } terminal) outcome = terminal;
                    switch (evt.Kind)
                    {
                        case AgentEventKind.MessageDelta when evt.Text is not null:
                            if (evt.Text.Length > 0) firstAnswerMs ??= Math.Round(watch.Elapsed.TotalMilliseconds, 1);
                            answer.Append(evt.Text);
                            break;
                        case AgentEventKind.ToolRequested or AgentEventKind.ToolStarted when evt.ToolName is not null:
                            var key = evt.ToolCallId?.ToString() ?? Guid.NewGuid().ToString("N");
                            if (!pending.ContainsKey(key)) { pending[key] = calls.Count; calls.Add(new(evt.ToolName, null, null)); }
                            break;
                        case AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed:
                            var id = evt.ToolCallId?.ToString() ?? "";
                            var index = pending.TryGetValue(id, out var found) ? found : calls.Count;
                            var status = evt.ToolStatus?.ToString() ?? (evt.Kind == AgentEventKind.ToolCompleted ? "Succeeded" : "Failed");
                            var call = new ToolCall(evt.ToolName ?? (index < calls.Count ? calls[index].Tool : "?"), status, evt.ErrorCode);
                            if (index < calls.Count) calls[index] = call; else calls.Add(call);
                            break;
                        case AgentEventKind.ReasoningCompleted:
                            reasoningTokens = evt.ReasoningTokens;
                            reasoningTruncated = evt.ReasoningTruncatedByBudget;
                            reasoningBlocks++;
                            if (evt.ReasoningTruncatedByBudget == true) reasoningBlocksTruncated++;
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                outcome = AgentTurnOutcome.TimedOut;
                runtimeError ??= "LabTaskTimeout";
            }
            finally
            {
                await runtime.CloseSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
                mongoCommands = mongoAccess.EndTools();
            }
            watch.Stop();
            var finalAnswer = answer.ToString().Trim();
            var called = calls.Select(call => call.Tool).ToHashSet(StringComparer.Ordinal);
            var missingTools = (task.ExpectTools ?? []).Where(tool => !called.Contains(tool)).ToArray();
            var succeededTools = calls.Where(call => call.Status == "Succeeded").Select(call => call.Tool).ToHashSet(StringComparer.Ordinal);
            var expectedSucceeded = (task.ExpectTools ?? []).All(succeededTools.Contains);
            var forbiddenAvoided = !(task.ForbidTools ?? []).Any(called.Contains);
            var missingParts = (task.ExpectAnswerContains ?? [])
                .Where(part => finalAnswer.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            var allSucceeded = calls.All(call => call.Status == "Succeeded");
            var completed = outcome == AgentTurnOutcome.Completed;
            var passed = completed && missingTools.Length == 0 && expectedSucceeded && forbiddenAvoided && missingParts.Length == 0 &&
                !mongoCommands.Any(command => command.Prohibited);
            results.Add(new(task.Id, (outcome ?? AgentTurnOutcome.OutcomeUnknown).ToString(), runtimeError, calls.ToArray(),
                finalAnswer, finalAnswer.Length, reasoningTokens, reasoningTruncated, ports.Proposals.Count - proposalCount,
                Math.Round(watch.Elapsed.TotalMilliseconds, 1), missingTools.Length == 0, expectedSucceeded, forbiddenAvoided, missingParts.Length == 0,
                allSucceeded, passed, missingTools, missingParts, mongoCommands)
            {
                Seed = seed, FirstAnswerMs = firstAnswerMs, ReasoningBlocks = reasoningBlocks, ReasoningBlocksTruncated = reasoningBlocksTruncated,
            });
            Console.Error.WriteLine($"info agent.run task={task.Id} seed={seed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} " +
                $"outcome={results[^1].Outcome} tools={calls.Count} passed={passed} ms={results[^1].DurationMs:0}");
        }
        var modelStatus = models.Status;
        var environment = EnvironmentReport.Create();
        var summary = Summarize(results, runOptions);
        return new(ReportSchema, Path.GetFileName(packagePath), model.Name, modelStatus.Provider, modelStatus.Backend?.ToString(), true, hash,
            summary, results, environment.IdeCommit ?? "", environment.GenAiVersion);
    }

    /// <summary>Configuração do laboratório para o provider local: só expõe as settings; não gera autocomplete.</summary>
    private sealed class LabAutocompleteSettings(AutocompleteSettings settings) : IAutocompleteService
    {
        public AutocompleteSettings Settings { get; } = settings;
        public LocalModelStatus Status => new(LocalModelState.Ready, "");
        public event EventHandler? SettingsChanged { add { } remove { } }
        public Task ConfigureAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Settings fixas no laboratório.");
        public Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<AutocompleteResult?>(null);
        public Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
    }

    private sealed class LabSecretStore : ISecretStore
    {
        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.UnsupportedPlatform));
        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Laboratório não acessa cofre.");
        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Laboratório não acessa cofre.");
        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Laboratório não acessa cofre.");
    }
}
