using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class KapiLabCommandLine
{
    private const int MaximumInputArtifactBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(false, true);

    public static RootCommand Build()
    {
        var root = new RootCommand("Console de laboratório dos contratos e modelos da IDE.");
        var workspace = new Option<string?>("--workspace") { Description = "Raiz explícita do workspace KapiCoder-Mongo." };
        workspace.Recursive = true;
        root.Options.Add(workspace);
        root.Subcommands.Add(BuildEnvironmentCommand(workspace));
        root.Subcommands.Add(BuildModelCommand(workspace));
        root.Subcommands.Add(BuildBenchCommand(workspace));
        root.Subcommands.Add(BuildContractCommand(workspace));
        root.Subcommands.Add(BuildCatalogCommand(workspace));
        root.Subcommands.Add(BuildReportCommand(workspace));
        root.Subcommands.Add(BuildNpuCommand());
        root.Subcommands.Add(BuildGuidanceCommand(workspace));
        root.Subcommands.Add(BuildSamplesCommand(workspace));
        root.Subcommands.Add(BuildParityCommand(workspace));
        root.Subcommands.Add(BuildAgentCommand(workspace));
        root.Subcommands.Add(BuildMatrixCommand(workspace));
        return root;
    }

    private static Command BuildMatrixCommand(Option<string?> workspace)
    {
        var matrix = new Command("matrix", "Executa casos de processo locais em sequência com ambiente isolado.");
        var run = new Command("run", "Executa matriz kapilab-process-matrix-v1 sem shell e preserva relatório parcial.");
        var input = new Option<string>("--in") { Description = "Arquivo kapilab-process-matrix-v1 dentro do workspace.", Required = true };
        var output = new Option<string?>("--out") { Description = "Relatório JSON em data/lab, reports/lab ou tmp; padrão stdout." };
        run.Options.Add(input);
        run.Options.Add(output);
        run.SetAction(parseResult => ExecuteAsync("matrix.run", () => ProcessMatrixCommand.RunAsync(
            parseResult.GetValue(input) ?? "", parseResult.GetValue(output), parseResult.GetValue(workspace))));
        matrix.Subcommands.Add(run);
        return matrix;
    }

    private static Command BuildAgentCommand(Option<string?> workspace)
    {
        var agent = new Command("agent", "Exercita um recorte sintético do boundary de tools do runtime de agentes.");
        var replay = new Command("replay", "Reexecuta casos sintéticos list_connections sem modelo e sem grants.");
        var input = new Option<string>("--in") { Description = "Arquivo kapilab-agent-replay-cases-v1 no workspace.", Required = true };
        var output = new Option<string?>("--out") { Description = "Relatório sintético em data/lab, reports/lab ou tmp; padrão stdout." };
        replay.Options.Add(input);
        replay.Options.Add(output);
        replay.SetAction(parseResult => ExecuteAsync("agent.replay", () => AgentReplayCommand.RunAsync(
            parseResult.GetValue(input) ?? "", parseResult.GetValue(output), parseResult.GetValue(workspace))));
        agent.Subcommands.Add(replay);
        return agent;
    }

    private static Command BuildParityCommand(Option<string?> workspace)
    {
        var parity = new Command("parity", "Compara observações já produzidas por ferramentas/runtime externos.");
        var compare = new Command("compare", "Compara tokenizer, prompt, greedy ou teacher sem executar inferência.");
        var input = new Option<string>("--in") { Description = "JSON kapilab-parity-observations-v1 dentro do workspace.", Required = true };
        var tolerances = new Option<string?>("--tolerances") { Description = "Tolerâncias JSON versionadas; obrigatórias para greedy e teacher." };
        var output = new Option<string?>("--out") { Description = "Relatório JSON em data/lab, reports/lab ou tmp; padrão stdout." };
        compare.Options.Add(input);
        compare.Options.Add(tolerances);
        compare.Options.Add(output);
        compare.SetAction(parseResult => ExecuteAsync("parity.compare", () => Task.FromResult(ParityCommand.Run(
            parseResult.GetValue(input) ?? "", parseResult.GetValue(tolerances), parseResult.GetValue(output), parseResult.GetValue(workspace)))));
        parity.Subcommands.Add(compare);
        return parity;
    }

    private static Command BuildSamplesCommand(Option<string?> workspace)
    {
        var samples = new Command("samples", "Gera amostras sintéticas isoladas para laboratório.");
        var generate = new Command("generate", "Gera exemplos determinísticos; não executa consultas nem lê datasets.");
        var seed = new Option<int>("--seed") { Description = "Semente raiz inteira.", Required = true };
        var count = new Option<int>("--count") { Description = "Quantidade de registros (1–10000).", DefaultValueFactory = _ => 100 };
        var index = new Option<int?>("--index") { Description = "Gera apenas este índice reproduzível; exige --count 1." };
        var output = new Option<string?>("--out") { Description = "Arquivo JSONL em data/lab, reports/lab ou tmp; padrão stdout." };
        generate.Options.Add(seed);
        generate.Options.Add(count);
        generate.Options.Add(index);
        generate.Options.Add(output);
        generate.SetAction(parseResult => ExecuteAsync("samples.generate", () => SamplesGenerateCommand.RunAsync(
            parseResult.GetValue(seed), parseResult.GetValue(count), parseResult.GetValue(index), parseResult.GetValue(output), parseResult.GetValue(workspace))));
        samples.Subcommands.Add(generate);
        return samples;
    }

    private static Command BuildReportCommand(Option<string?> workspace)
    {
        var report = new Command("report", "Avalia evidências contra um artefato JSON de gates já versionado.");
        var evaluate = new Command("evaluate", "Gera relatório de gates; gates YAML devem ser convertidos fora do KapiLab.");
        var gates = new Option<string>("--gates") { Description = "Arquivo JSON de gates derivado e versionado.", Required = true };
        var evidence = new Option<string>("--evidence") { Description = "Arquivo JSON de evidência de uma execução.", Required = true };
        var output = new Option<string>("--out") { Description = "Saída em reports/lab, data/lab ou tmp.", Required = true };
        evaluate.Options.Add(gates);
        evaluate.Options.Add(evidence);
        evaluate.Options.Add(output);
        evaluate.SetAction(parseResult => ExecuteAsync("report.evaluate", () =>
        {
            var root = LabWorkspace.Resolve(parseResult.GetValue(workspace));
            if (root is null)
            {
                Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE.");
                return Task.FromResult((int)ExitCode.Usage);
            }
            var gatePath = ReadArtifactPath(parseResult.GetValue(gates)!, root);
            var evidencePath = ReadArtifactPath(parseResult.GetValue(evidence)!, root);
            var outputPath = LabWorkspace.ResolveOutput(root, parseResult.GetValue(output)!);
            LabWorkspace.RefuseInputOverwrite(root, outputPath, [gatePath, evidencePath]);
            var evaluation = GateEvaluation.Evaluate(
                LabWorkspace.ReadUtf8FileLimited(gatePath, MaximumInputArtifactBytes),
                LabWorkspace.ReadUtf8FileLimited(evidencePath, MaximumInputArtifactBytes), root, outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, GateEvaluation.Serialize(evaluation), new System.Text.UTF8Encoding(false));
                File.Move(temporaryPath, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            WriteJson(new { schema = evaluation.Schema, evaluation.RunId, evaluation.Qualified,
                evaluation.Complete, evaluation.Passed, gateCount = evaluation.Gates.Count, output = Path.GetRelativePath(root, outputPath) });
            return Task.FromResult(evaluation.Passed ? (int)ExitCode.Success : (int)ExitCode.GateFailed);
        }));
        report.Subcommands.Add(evaluate);

        var merge = new Command("merge", "Une relatórios locais compatíveis, preservando proveniência e resultados individuais.");
        var reportPaths = new Argument<string[]>("reports")
        {
            Description = "Dois ou mais caminhos de relatório JSON dentro do workspace.",
            Arity = ArgumentArity.OneOrMore,
        };
        var mergeOutput = new Option<string>("--out") { Description = "Arquivo de saída em reports/lab, data/lab ou tmp.", Required = true };
        merge.Arguments.Add(reportPaths);
        merge.Options.Add(mergeOutput);
        merge.SetAction(parseResult => ExecuteAsync("report.merge", () =>
        {
            var root = LabWorkspace.Resolve(parseResult.GetValue(workspace));
            if (root is null)
            {
                Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE.");
                return Task.FromResult((int)ExitCode.Usage);
            }
            var inputPaths = parseResult.GetValue(reportPaths) ?? [];
            if (inputPaths.Length < 2)
            {
                Console.Error.WriteLine("error informe pelo menos dois relatórios.");
                return Task.FromResult((int)ExitCode.Usage);
            }
            ReportMerge.ValidateInputLimits(inputPaths.Length, 0);
            var resolvedInputs = inputPaths.Select(path => ReadArtifactPath(path, root)).ToArray();
            var outputPath = LabWorkspace.ResolveOutput(root, parseResult.GetValue(mergeOutput)!);
            LabWorkspace.RefuseInputOverwrite(root, outputPath, resolvedInputs);

            long measuredCharacters = 0;
            foreach (var inputPath in resolvedInputs)
            {
                measuredCharacters += CountTextCharacters(inputPath, ReportMerge.MaximumReportCharacters);
                ReportMerge.ValidateInputLimits(resolvedInputs.Length, measuredCharacters);
            }

            var reports = new List<string>(resolvedInputs.Length);
            long loadedCharacters = 0;
            foreach (var inputPath in resolvedInputs)
            {
                var remainingCharacters = ReportMerge.MaximumTotalCharacters - loadedCharacters;
                var perReportLimit = (int)Math.Min(ReportMerge.MaximumReportCharacters, remainingCharacters);
                var report = ReadTextBounded(inputPath, perReportLimit);
                loadedCharacters += report.Length;
                ReportMerge.ValidateInputLimits(resolvedInputs.Length, loadedCharacters);
                reports.Add(report);
            }
            var merged = ReportMerge.Merge(reports);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, ReportMerge.Serialize(merged), new System.Text.UTF8Encoding(false));
                File.Move(temporaryPath, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            WriteJson(new { schema = merged.Schema, merged.Qualified, merged.Complete, merged.Passed,
                merged.Outcome, reportCount = merged.Reports.Count, output = Path.GetRelativePath(root, outputPath) });
            return Task.FromResult(merged.Passed ? (int)ExitCode.Success : (int)ExitCode.GateFailed);
        }));
        report.Subcommands.Add(merge);
        return report;
    }

    internal static string ReadArtifactPath(string path, string workspace)
    {
        var fullPath = Path.GetFullPath(path, workspace);
        var relative = Path.GetRelativePath(workspace, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Artefatos de entrada devem estar dentro do workspace.");
        LabWorkspace.RefuseBlindInput(fullPath, workspace);
        if (new FileInfo(fullPath).Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Artefato excede o limite de leitura.");
        return fullPath;
    }

    private static int CountTextCharacters(string path, int maximumCharacters)
    {
        using var reader = new StreamReader(path, StrictUtf8, detectEncodingFromByteOrderMarks: false);
        var buffer = new char[8 * 1024];
        var total = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maximumCharacters)
                throw new InvalidDataException("Relatório vazio ou acima do limite permitido.");
        }
        return total;
    }

    private static string ReadTextBounded(string path, int maximumCharacters)
    {
        using var reader = new StreamReader(path, StrictUtf8, detectEncodingFromByteOrderMarks: false);
        var builder = new System.Text.StringBuilder(Math.Min(maximumCharacters, 8 * 1024));
        var buffer = new char[8 * 1024];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (builder.Length + read > maximumCharacters)
                throw new InvalidDataException("Tamanho total dos relatórios excede o limite permitido.");
            builder.Append(buffer, 0, read);
        }
        return builder.ToString();
    }

    private static Command BuildCatalogCommand(Option<string?> workspace)
    {
        var catalog = new Command("catalog", "Valida snapshots versionados de catálogo de tools.");
        var plans = new Command("plans", "Calcula planos sintéticos pela política vigente da IDE; não invoca tools.");
        var planInput = new Option<string>("--in") { Description = "Casos JSON kapilab-agent-plan-cases-v1 dentro do workspace.", Required = true };
        var planOutput = new Option<string?>("--out") { Description = "Snapshot JSON em reports/lab, data/lab ou tmp; padrão stdout." };
        plans.Options.Add(planInput);
        plans.Options.Add(planOutput);
        plans.SetAction(parseResult => ExecuteAsync("catalog.plans", () =>
        {
            var root = LabWorkspace.Resolve(parseResult.GetValue(workspace));
            if (root is null)
            {
                Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE.");
                return Task.FromResult((int)ExitCode.Usage);
            }
            var inputPath = ReadArtifactPath(parseResult.GetValue(planInput)!, root);
            var input = AgentPlanSnapshot.ParseInput(LabWorkspace.ReadUtf8FileLimited(inputPath, 1024 * 1024));
            if (input is null)
            {
                Console.Error.WriteLine("error casos de plano inválidos ou schema incompatível.");
                return Task.FromResult((int)ExitCode.InvalidInput);
            }
            var snapshot = AgentPlanSnapshot.Evaluate(input);
            if (parseResult.GetValue(planOutput) is not { Length: > 0 } output)
            {
                WriteJson(snapshot);
                return Task.FromResult((int)ExitCode.Success);
            }

            var outputPath = LabWorkspace.ResolveOutput(root, output);
            LabWorkspace.RefuseInputOverwrite(root, outputPath, [inputPath]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, AgentPlanSnapshot.Serialize(snapshot), new System.Text.UTF8Encoding(false));
                File.Move(temporaryPath, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            WriteJson(new { schema = snapshot.Schema, evidenceKind = snapshot.EvidenceKind,
                caseCount = snapshot.Cases.Length, output = Path.GetRelativePath(root, outputPath),
                toolInvocationPerformed = snapshot.ToolInvocationPerformed });
            return Task.FromResult((int)ExitCode.Success);
        }));
        catalog.Subcommands.Add(plans);

        var export = new Command("export", "Exporta a superfície máxima de ferramentas da registry composta; não inclui grants ou plano de execução.");
        var exportProvider = new Option<string>("--provider") { Description = "ID do provider registrado na composição local.", Required = true };
        var exportSurface = new Option<string>("--surface") { Description = "Superfície in-process ou session-channel.", Required = true };
        var exportOutput = new Option<string?>("--out") { Description = "Snapshot JSON em data/lab, reports/lab ou tmp; padrão stdout." };
        export.Options.Add(exportProvider);
        export.Options.Add(exportSurface);
        export.Options.Add(exportOutput);
        export.SetAction(parseResult => ExecuteAsync("catalog.export", () => AgentCatalogExportCommand.RunAsync(
            parseResult.GetValue(exportProvider) ?? "", parseResult.GetValue(exportSurface) ?? "",
            parseResult.GetValue(exportOutput), parseResult.GetValue(workspace))));
        catalog.Subcommands.Add(export);

        var exportPlan = new Command("export-plan", "Exporta descritores in-process limitados por um único plano sintético; não verifica grants reais.");
        var planProvider = new Option<string>("--provider") { Description = "ID do provider; deve corresponder ao provider do plano sintético.", Required = true };
        var planInputPath = new Option<string>("--in") { Description = "Entrada JSON kapilab-agent-catalog-plan-input-v1 dentro do workspace.", Required = true };
        var exportPlanOutput = new Option<string?>("--out") { Description = "Snapshot JSON em data/lab, reports/lab ou tmp; padrão stdout." };
        exportPlan.Options.Add(planProvider);
        exportPlan.Options.Add(planInputPath);
        exportPlan.Options.Add(exportPlanOutput);
        exportPlan.SetAction(parseResult => ExecuteAsync("catalog.export-plan", () => AgentCatalogPlanExportCommand.RunAsync(
            parseResult.GetValue(planProvider) ?? "", parseResult.GetValue(planInputPath) ?? "",
            parseResult.GetValue(exportPlanOutput), parseResult.GetValue(workspace))));
        catalog.Subcommands.Add(exportPlan);

        var invoke = new Command("invoke", "Invoca tools de metadados permitidas pela registry real; casos restantes exercitam negação sem grants.");
        var invokeInput = new Option<string>("--in") { Description = "Casos JSON kapilab-agent-catalog-invocation-cases-v1 dentro do workspace.", Required = true };
        var invokeOutput = new Option<string?>("--out") { Description = "Relatório JSON em reports/lab, data/lab ou tmp; padrão stdout." };
        invoke.Options.Add(invokeInput);
        invoke.Options.Add(invokeOutput);
        invoke.SetAction(parseResult => ExecuteAsync("catalog.invoke", () => AgentCatalogInvokeCommand.RunAsync(
            parseResult.GetValue(invokeInput) ?? "", parseResult.GetValue(invokeOutput), parseResult.GetValue(workspace))));
        catalog.Subcommands.Add(invoke);

        var check = new Command("check", "Compara dois snapshots provider-maximum sem inferir plano ou grants.");
        var expected = new Option<string>("--expected") { Description = "Snapshot de referência JSON.", Required = true };
        var actual = new Option<string>("--actual") { Description = "Snapshot atual JSON.", Required = true };
        check.Options.Add(expected);
        check.Options.Add(actual);
        check.SetAction(parseResult => ExecuteAsync("catalog.check", () =>
        {
            var root = LabWorkspace.Resolve(parseResult.GetValue(workspace));
            if (root is null)
            {
                Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE.");
                return Task.FromResult((int)ExitCode.Usage);
            }
            var expectedPath = ReadSnapshotPath(parseResult.GetValue(expected)!, root);
            var actualPath = ReadSnapshotPath(parseResult.GetValue(actual)!, root);
            var expectedSnapshot = AgentToolCatalogSnapshot.Parse(LabWorkspace.ReadUtf8FileLimited(expectedPath, MaximumInputArtifactBytes));
            var actualSnapshot = AgentToolCatalogSnapshot.Parse(LabWorkspace.ReadUtf8FileLimited(actualPath, MaximumInputArtifactBytes));
            if (expectedSnapshot is null || actualSnapshot is null)
            {
                Console.Error.WriteLine("error snapshot inválido ou schema incompatível.");
                return Task.FromResult((int)ExitCode.InvalidInput);
            }
            var result = AgentToolCatalogSnapshot.Check(expectedSnapshot, actualSnapshot);
            WriteJson(new { schema = "kapilab-agent-catalog-check-v1", result.Matches, result.Differences,
                complete = false, scope = "provider-maximum" });
            return Task.FromResult(result.Matches ? (int)ExitCode.Success : (int)ExitCode.Drift);
        }));
        catalog.Subcommands.Add(check);
        return catalog;
    }

    private static string ReadSnapshotPath(string path, string workspace)
    {
        LabWorkspace.RefuseBlindInput(path, workspace);
        var fullPath = Path.GetFullPath(path, workspace);
        if (new FileInfo(fullPath).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Snapshot excede o limite de leitura.");
        return fullPath;
    }

    private static Command BuildNpuCommand()
    {
        var npu = new Command("npu", "Inspeciona providers de execução disponíveis sem carregar modelos.");
        var inventory = new Command("inventory", "Lista providers e dispositivos que o ONNX Runtime deste build informa.");
        inventory.SetAction(_ => ExecuteAsync("npu.inventory", () =>
        {
            var report = NpuInventoryReport.Create(EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxHardwareProbe.Detect());
            WriteJson(report);
            return Task.FromResult((int)ExitCode.Success);
        }));
        npu.Subcommands.Add(inventory);
        return npu;
    }

    private static Command BuildGuidanceCommand(Option<string?> workspace)
    {
        var guidance = new Command("guidance", "Testa guidance gramatical experimental no runtime GenAI.");
        var smoke = new Command("smoke", "Compara geração baseline e Lark guidance sem emitir texto gerado.");
        var package = new Option<string>("--package") { Description = "Diretório do pacote ONNX Runtime GenAI.", Required = true };
        var prompt = new Option<string>("--prompt-file") { Description = "Arquivo UTF-8 do prompt dentro do workspace; conteúdo não é ecoado.", Required = true };
        var grammar = new Option<string>("--grammar-file") { Description = "Arquivo UTF-8 da gramática Lark dentro do workspace; conteúdo não é ecoado.", Required = true };
        var device = new Option<string>("--device") { Description = "Provider explícito: cpu ou gpu.", DefaultValueFactory = _ => "cpu" };
        var maximumTokens = new Option<int>("--max-tokens") { Description = "Limite de geração (1–4096).", DefaultValueFactory = _ => 64 };
        smoke.Options.Add(package);
        smoke.Options.Add(prompt);
        smoke.Options.Add(grammar);
        smoke.Options.Add(device);
        smoke.Options.Add(maximumTokens);
        smoke.SetAction(parseResult => ExecuteAsync("guidance.smoke", () => GuidanceSmokeCommand.RunAsync(
            parseResult.GetValue(package) ?? "", parseResult.GetValue(prompt) ?? "", parseResult.GetValue(grammar) ?? "",
            parseResult.GetValue(device) ?? "cpu", parseResult.GetValue(maximumTokens), parseResult.GetValue(workspace))));
        guidance.Subcommands.Add(smoke);
        return guidance;
    }

    private static Command BuildEnvironmentCommand(Option<string?> workspace)
    {
        var command = new Command("env", "Exibe a proveniência segura do build e do ambiente.");
        var expectedGenAi = new Option<string?>("--expect-genai") { Description = "Falha com código 7 se a versão GenAI divergir." };
        var expectedBackend = new Option<string?>("--expect-backend") { Description = "Falha com código 7 se o backend divergir." };
        command.Options.Add(expectedGenAi);
        command.Options.Add(expectedBackend);
        command.SetAction(parseResult => ExecuteAsync("env", () =>
        {
            var report = EnvironmentReport.Create();
            WriteJson(report);
            var mismatch = (parseResult.GetValue(expectedGenAi) is { } genAi &&
                            !string.Equals(genAi, report.GenAiVersion, StringComparison.OrdinalIgnoreCase)) ||
                           (parseResult.GetValue(expectedBackend) is { } backend &&
                            !string.Equals(backend, report.Backend, StringComparison.OrdinalIgnoreCase));
            if (parseResult.GetValue(workspace) is { Length: > 0 } path)
                Console.Error.WriteLine($"info workspace={Path.GetFullPath(path)}");
            return Task.FromResult(mismatch ? (int)ExitCode.Drift : (int)ExitCode.Success);
        }));
        return command;
    }

    private static Command BuildModelCommand(Option<string?> workspace)
    {
        var model = new Command("model", "Inspeciona e valida pacotes sem carregá-los.");
        model.Subcommands.Add(BuildModelInspectionCommand("inspect", "Inspeciona pacote pelo catálogo real da IDE.", workspace));
        model.Subcommands.Add(BuildModelInspectionCommand("validate", "Valida pacote pelo catálogo real da IDE.", workspace));
        model.Subcommands.Add(BuildModelRunCommand(workspace));
        return model;
    }

    private static Command BuildBenchCommand(Option<string?> workspace)
    {
        var bench = new Command("bench", "Mede execuções explícitas dos caminhos reais da IDE.");
        var autocomplete = new Command("autocomplete", "Mede inferência local de autocomplete com aquecimento fora da amostra.");
        var package = new Option<string?>("--package") { Description = "Pasta do pacote de modelo ONNX Runtime GenAI; dispensada com --matrix." };
        var input = new Option<string>("--in") { Description = "Arquivo JSONL com registros FIM/completion.", Required = true };
        var benchMatrix = new Option<string?>("--matrix") { Description = "Matriz JSON de células package×hardware; cada célula usa processo isolado." };
        var output = new Option<string?>("--out") { Description = "Saída JSONL em data/lab, reports/lab ou tmp; padrão stdout." };
        var device = new Option<string>("--device") { Description = "Hardware: auto, cpu, gpu ou npu.", DefaultValueFactory = _ => "auto" };
        var viaQueue = new Option<bool>("--via-queue") { Description = "Autoriza GPU sob coordenação local da fila (exige KAPILAB_GPU_TOKEN)." };
        var standaloneGpu = new Option<bool>("--standalone-gpu") { Description = "Executa GPU standalone, respeitando tmp/gpu.pause." };
        var contextTokens = new Option<int>("--context-tokens") { Description = "Orçamento de contexto da IDE.", DefaultValueFactory = _ => 2048 };
        var maximumTokens = new Option<int>("--max-tokens") { Description = "Limite de tokens de autocomplete.", DefaultValueFactory = _ => 32 };
        var limit = new Option<int?>("--limit") { Description = "Quantidade máxima de registros de entrada." };
        var warmup = new Option<int>("--warmup") { Description = "Gerações de aquecimento no primeiro registro, fora das amostras; padrão 1.", DefaultValueFactory = _ => 1 };
        var iterations = new Option<int>("--iterations") { Description = "Repetições medidas de cada registro; padrão 1.", DefaultValueFactory = _ => 1 };
        autocomplete.Options.Add(package);
        autocomplete.Options.Add(input);
        autocomplete.Options.Add(benchMatrix);
        autocomplete.Options.Add(output);
        autocomplete.Options.Add(device);
        autocomplete.Options.Add(viaQueue);
        autocomplete.Options.Add(standaloneGpu);
        autocomplete.Options.Add(contextTokens);
        autocomplete.Options.Add(maximumTokens);
        autocomplete.Options.Add(limit);
        autocomplete.Options.Add(warmup);
        autocomplete.Options.Add(iterations);
        autocomplete.SetAction(parseResult => ExecuteAsync("bench.autocomplete", () =>
        {
            var matrixPath = parseResult.GetValue(benchMatrix);
            if (matrixPath is not null)
            {
                if (parseResult.GetValue(package) is not null) return Task.FromResult((int)ExitCode.Usage);
                return BenchMatrixCommand.RunAsync(matrixPath, parseResult.GetValue(input) ?? "", parseResult.GetValue(output),
                    parseResult.GetValue(workspace), "autocomplete", parseResult.GetValue(contextTokens), parseResult.GetValue(maximumTokens),
                    parseResult.GetValue(limit), parseResult.GetValue(warmup), parseResult.GetValue(iterations),
                    parseResult.GetValue(viaQueue), parseResult.GetValue(standaloneGpu));
            }
            if (parseResult.GetValue(package) is not { } selectedPackage) return Task.FromResult((int)ExitCode.Usage);
            return AutocompleteBenchCommand.RunAsync(selectedPackage, parseResult.GetValue(input) ?? "", parseResult.GetValue(output),
                parseResult.GetValue(workspace), parseResult.GetValue(device) ?? "auto", parseResult.GetValue(contextTokens),
                parseResult.GetValue(maximumTokens), parseResult.GetValue(limit), parseResult.GetValue(warmup),
                parseResult.GetValue(iterations), parseResult.GetValue(viaQueue), parseResult.GetValue(standaloneGpu));
        }));
        bench.Subcommands.Add(autocomplete);

        var chat = new Command("chat", "Mede chat Qwen3 isolado, sem gravar perguntas ou respostas.");
        var chatPackage = new Option<string?>("--package") { Description = "Pasta do pacote Qwen3 com formatter de chat; dispensada com --matrix." };
        var chatInput = new Option<string>("--in") { Description = "JSONL kapilab-chat-input-v1.", Required = true };
        var chatMatrix = new Option<string?>("--matrix") { Description = "Matriz JSON de células package×hardware; cada célula usa processo isolado." };
        var chatDevice = new Option<string>("--device") { Description = "Hardware: auto, cpu, gpu ou npu.", DefaultValueFactory = _ => "auto" };
        var chatViaQueue = new Option<bool>("--via-queue") { Description = "Autoriza GPU sob coordenação local da fila (exige KAPILAB_GPU_TOKEN)." };
        var chatStandaloneGpu = new Option<bool>("--standalone-gpu") { Description = "Executa GPU standalone, respeitando tmp/gpu.pause." };
        var chatContextTokens = new Option<int?>("--context-tokens") { Description = "Orçamento de contexto por chamada (64 até o limite do pacote); padrão: janela do pacote." };
        var chatMaximumTokens = new Option<int>("--max-tokens") { Description = "Máximo de tokens de saída, de 1 a 4096.", DefaultValueFactory = _ => 256 };
        var chatLimit = new Option<int?>("--limit") { Description = "Prefixo de até 100 registros." };
        var chatWarmup = new Option<int>("--warmup") { Description = "Aquecimentos no primeiro registro, de 0 a 5; padrão 1.", DefaultValueFactory = _ => 1 };
        var chatIterations = new Option<int>("--iterations") { Description = "Repetições medidas por registro, de 1 a 10; padrão 1.", DefaultValueFactory = _ => 1 };
        chat.Options.Add(chatPackage);
        chat.Options.Add(chatInput);
        chat.Options.Add(chatMatrix);
        chat.Options.Add(chatDevice);
        chat.Options.Add(chatViaQueue);
        chat.Options.Add(chatStandaloneGpu);
        chat.Options.Add(chatContextTokens);
        chat.Options.Add(chatMaximumTokens);
        chat.Options.Add(chatLimit);
        chat.Options.Add(chatWarmup);
        chat.Options.Add(chatIterations);
        chat.SetAction(parseResult => ExecuteAsync("bench.chat", () =>
        {
            var matrixPath = parseResult.GetValue(chatMatrix);
            if (matrixPath is not null)
            {
                if (parseResult.GetValue(chatPackage) is not null) return Task.FromResult((int)ExitCode.Usage);
                return BenchMatrixCommand.RunAsync(matrixPath, parseResult.GetValue(chatInput) ?? "", null,
                    parseResult.GetValue(workspace), "chat", 0, parseResult.GetValue(chatMaximumTokens),
                    parseResult.GetValue(chatLimit), parseResult.GetValue(chatWarmup), parseResult.GetValue(chatIterations),
                    parseResult.GetValue(chatViaQueue), parseResult.GetValue(chatStandaloneGpu), parseResult.GetValue(chatContextTokens));
            }
            if (parseResult.GetValue(chatPackage) is not { } selectedPackage) return Task.FromResult((int)ExitCode.Usage);
            return BenchChatCommand.RunAsync(selectedPackage, parseResult.GetValue(chatInput) ?? "", parseResult.GetValue(workspace),
                parseResult.GetValue(chatDevice) ?? "auto", parseResult.GetValue(chatMaximumTokens), parseResult.GetValue(chatLimit),
                parseResult.GetValue(chatWarmup), parseResult.GetValue(chatIterations), parseResult.GetValue(chatViaQueue), parseResult.GetValue(chatStandaloneGpu),
                parseResult.GetValue(chatContextTokens));
        }));
        bench.Subcommands.Add(chat);
        return bench;
    }

    private static Command BuildModelRunCommand(Option<string?> workspace)
    {
        var run = new Command("run", "Executa inferência explícita pelo serviço real da IDE.");
        var autocomplete = new Command("autocomplete", "Executa autocomplete sobre registros FIM JSONL.");
        var package = new Option<string>("--package") { Description = "Pasta do pacote de modelo ONNX Runtime GenAI.", Required = true };
        var input = new Option<string>("--in") { Description = "Arquivo JSONL com registros FIM/completion.", Required = true };
        var output = new Option<string?>("--out") { Description = "Saída JSONL em data/lab, reports/lab ou tmp; padrão stdout." };
        var keepText = new Option<bool>("--keep-text") { Description = "Inclui a saída bruta apenas para a fixture sintética verificada; exige --out explícito." };
        var device = new Option<string>("--device") { Description = "Hardware: auto, cpu, gpu ou npu.", DefaultValueFactory = _ => "auto" };
        var viaQueue = new Option<bool>("--via-queue") { Description = "Autoriza GPU sob coordenação local da fila (exige KAPILAB_GPU_TOKEN)." };
        var standaloneGpu = new Option<bool>("--standalone-gpu") { Description = "Executa GPU standalone, respeitando tmp/gpu.pause." };
        var contextTokens = new Option<int>("--context-tokens") { Description = "Orçamento de contexto da IDE.", DefaultValueFactory = _ => 2048 };
        var maximumTokens = new Option<int>("--max-tokens") { Description = "Limite de tokens de autocomplete.", DefaultValueFactory = _ => 32 };
        var limit = new Option<int?>("--limit") { Description = "Quantidade máxima explícita de registros." };
        autocomplete.Options.Add(package);
        autocomplete.Options.Add(input);
        autocomplete.Options.Add(output);
        autocomplete.Options.Add(keepText);
        autocomplete.Options.Add(device);
        autocomplete.Options.Add(viaQueue);
        autocomplete.Options.Add(standaloneGpu);
        autocomplete.Options.Add(contextTokens);
        autocomplete.Options.Add(maximumTokens);
        autocomplete.Options.Add(limit);
        autocomplete.SetAction(parseResult => ExecuteAsync("model.run.autocomplete", () => ModelRunCommand.RunAutocompleteAsync(
            parseResult.GetValue(package) ?? "", parseResult.GetValue(input) ?? "", parseResult.GetValue(workspace),
            parseResult.GetValue(device) ?? "auto", parseResult.GetValue(contextTokens), parseResult.GetValue(maximumTokens),
            parseResult.GetValue(limit), parseResult.GetValue(viaQueue), parseResult.GetValue(standaloneGpu),
            parseResult.GetValue(output), parseResult.GetValue(keepText))));
        run.Subcommands.Add(autocomplete);

        var chat = new Command("chat", "Gera uma rodada de chat Qwen3 por registro JSONL, sem tools nem histórico.");
        var chatPackage = new Option<string>("--package") { Description = "Pasta do pacote Qwen3 com template de chat da IDE.", Required = true };
        var chatInput = new Option<string>("--in") { Description = "JSONL kapilab-chat-input-v1 (id e mensagem de usuário).", Required = true };
        var chatDevice = new Option<string>("--device") { Description = "Hardware: auto, cpu, gpu ou npu.", DefaultValueFactory = _ => "auto" };
        var chatViaQueue = new Option<bool>("--via-queue") { Description = "Autoriza GPU sob coordenação local da fila (exige KAPILAB_GPU_TOKEN)." };
        var chatStandaloneGpu = new Option<bool>("--standalone-gpu") { Description = "Executa GPU standalone, respeitando tmp/gpu.pause." };
        var chatContextTokens = new Option<int?>("--context-tokens") { Description = "Orçamento de contexto por chamada (64 até o limite do pacote); padrão: janela do pacote." };
        var chatMaximumTokens = new Option<int>("--max-tokens") { Description = "Máximo explícito de tokens de saída, de 1 a 4096.", DefaultValueFactory = _ => 256 };
        var chatLimit = new Option<int?>("--limit") { Description = "Prefixo de registros a executar, de 1 a 100." };
        chat.Options.Add(chatPackage);
        chat.Options.Add(chatInput);
        chat.Options.Add(chatDevice);
        chat.Options.Add(chatViaQueue);
        chat.Options.Add(chatStandaloneGpu);
        chat.Options.Add(chatContextTokens);
        chat.Options.Add(chatMaximumTokens);
        chat.Options.Add(chatLimit);
        chat.SetAction(parseResult => ExecuteAsync("model.run.chat", () => ModelRunChatCommand.RunAsync(
            parseResult.GetValue(chatPackage) ?? "", parseResult.GetValue(chatInput) ?? "", parseResult.GetValue(workspace),
            parseResult.GetValue(chatDevice) ?? "auto", parseResult.GetValue(chatMaximumTokens), parseResult.GetValue(chatLimit),
            parseResult.GetValue(chatViaQueue), parseResult.GetValue(chatStandaloneGpu), parseResult.GetValue(chatContextTokens))));
        run.Subcommands.Add(chat);
        return run;
    }

    private static Command BuildModelInspectionCommand(string name, string description, Option<string?> workspace)
    {
        var command = new Command(name, description);
        var package = new Option<string?>("--package") { Description = "Diretório do pacote ONNX Runtime GenAI." };
        var modelsDir = new Option<string?>("--models-dir") { Description = "Diretório contendo subpastas de modelos instalados." };
        var includeHardware = new Option<bool>("--hardware") { Description = "Inclui sondagem de hardware disponível." };
        var strictKapi = new Option<bool>("--strict-kapi") { Description = "Valida o contrato local kapilab-kapi-model-v1." };
        var verifyHashes = new Option<bool>("--verify-hashes") { Description = "Verifica SHA-256 dos arquivos listados no manifesto local versionado." };
        command.Options.Add(package);
        command.Options.Add(modelsDir);
        command.Options.Add(includeHardware);
        if (name == "validate")
        {
            command.Options.Add(strictKapi);
            command.Options.Add(verifyHashes);
        }
        command.SetAction(parseResult => ExecuteAsync($"model.{name}", async () =>
        {
            var packagePath = parseResult.GetValue(package);
            var modelsPath = parseResult.GetValue(modelsDir);
            if (string.IsNullOrWhiteSpace(packagePath) == string.IsNullOrWhiteSpace(modelsPath))
            {
                Console.Error.WriteLine("error informe exatamente uma opção: --package ou --models-dir.");
                return (int)ExitCode.Usage;
            }

            var fileAccess = new KapiLabModelFileAccess();
            var catalog = new Infrastructure.LocalAi.LocalModelCatalog(modelsPath ?? packagePath, fileAccess: fileAccess);
            var paths = new List<string>();
            if (packagePath is not null) paths.Add(Path.GetFullPath(packagePath));
            else if (modelsPath is not null)
            {
                if (!fileAccess.DirectoryExists(modelsPath))
                {
                    Console.Error.WriteLine("error diretório de modelos inexistente.");
                    return (int)ExitCode.InvalidInput;
                }
                paths.AddRange(fileAccess.EnumerateDirectories(Path.GetFullPath(modelsPath)).Order(StringComparer.OrdinalIgnoreCase));
            }

            var results = new List<ModelInspection>();
            foreach (var path in paths)
            {
                var validation = await catalog.ValidateAsync(path).ConfigureAwait(false);
                var inspection = ModelInspection.From(validation, parseResult.GetValue(includeHardware));
                if (name == "validate")
                {
                    var strict = parseResult.GetValue(strictKapi)
                        ? StrictKapiModelValidation.ValidateMetadata(path, fileAccess) : null;
                    var hashes = parseResult.GetValue(verifyHashes)
                        ? StrictKapiModelValidation.VerifyHashes(path, fileAccess) : null;
                    inspection = inspection with
                    {
                        IdeSupported = inspection.State == "Available",
                        SchemaValid = strict?.Valid,
                        StrictSchema = strict is null ? null : StrictKapiModelValidation.MetadataSchema,
                        HashesValid = hashes?.Valid,
                        HashScope = hashes is null ? null : "manifest-listed-files",
                        VerifiedFiles = hashes?.VerifiedFiles,
                        ValidationIssues = strict is null && hashes is null ? null :
                            (strict?.Issues ?? []).Concat(hashes?.Issues ?? []).Distinct(StringComparer.Ordinal).ToArray(),
                    };
                }
                results.Add(inspection);
            }
            WriteJson(new ModelInspectionReport(name == "validate" ? "kapilab-model-validate-v1" : "kapilab-model-inspect-v1", results));
            return results.Count == 0 || results.Any(result => result.State != "Available" || result.SchemaValid == false || result.HashesValid == false)
                ? (int)ExitCode.PackageInvalid : (int)ExitCode.Success;
        }));
        return command;
    }

    private static Command BuildContractCommand(Option<string?> workspace)
    {
        var contract = new Command("contract", "Renderiza prompts com os builders reais da IDE.");
        var render = new Command("render", "Renderiza contratos reais da IDE para edição ou chat local legado.");
        var input = new Option<string>("--in") { Description = "Arquivo JSONL de registros.", Required = true };
        var contractName = new Option<string>("--contract") { Description = "Contrato da IDE.", DefaultValueFactory = _ => "editor-context-v1" };
        var output = new Option<string?>("--out") { Description = "Saída em data/lab ou reports/lab; padrão stdout." };
        var limit = new Option<int?>("--limit") { Description = "Número máximo de registros lidos." };
        var ids = new Option<string?>("--ids") { Description = "Lista de IDs separada por vírgulas." };
        var noEditorContext = new Option<bool>("--no-editor-context") { Description = "Renderiza sem o contexto do editor." };
        render.Options.Add(input);
        render.Options.Add(contractName);
        render.Options.Add(output);
        render.Options.Add(limit);
        render.Options.Add(ids);
        render.Options.Add(noEditorContext);
        render.SetAction(parseResult =>
        {
            var selectedContract = parseResult.GetValue(contractName) ?? "editor-context-v1";
            var command = selectedContract == LegacyChatRenderCommand.Contract ? "contract.render.legacy-chat" : "contract.render";
            return ExecuteAsync(command, () => selectedContract == LegacyChatRenderCommand.Contract
                ? LegacyChatRenderCommand.RunAsync(parseResult.GetValue(input) ?? "", parseResult.GetValue(output),
                    parseResult.GetValue(limit), parseResult.GetValue(ids), parseResult.GetValue(noEditorContext),
                    parseResult.GetValue(workspace))
                : ContractRenderCommand.RunAsync(parseResult.GetValue(input) ?? "", selectedContract, parseResult.GetValue(output),
                    parseResult.GetValue(limit), parseResult.GetValue(ids), parseResult.GetValue(noEditorContext),
                    parseResult.GetValue(workspace)));
        });
        contract.Subcommands.Add(render);

        var tokenize = new Command("tokenize", "Tokeniza um contrato renderizado com o adapter real do pacote GenAI.");
        var package = new Option<string>("--package") { Description = "Pasta de pacote ONNX GenAI compatível; a inicialização pode carregar o decoder.", Required = true };
        var tokenInput = new Option<string>("--in") { Description = "JSONL kapilab-render-v1 criado por contract render.", Required = true };
        var tokenOutput = new Option<string?>("--out") { Description = "Saída JSONL limitada ao workspace; padrão stdout." };
        var contextTokens = new Option<int>("--context-tokens") { Description = "Orçamento solicitado; o CLI calcula o limite efetivo da IDE.", DefaultValueFactory = _ => 2048 };
        var completionTokens = new Option<int>("--completion-tokens") { Description = "Teto de completion usado para calcular o contexto efetivo; padrão 32.", DefaultValueFactory = _ => 32 };
        var tokenLimit = new Option<int?>("--limit") { Description = "Quantidade máxima explícita de registros." };
        tokenize.Options.Add(package);
        tokenize.Options.Add(tokenInput);
        tokenize.Options.Add(tokenOutput);
        tokenize.Options.Add(contextTokens);
        tokenize.Options.Add(completionTokens);
        tokenize.Options.Add(tokenLimit);
        tokenize.SetAction(parseResult => ExecuteAsync("contract.tokenize", () => ContractTokenCommand.TokenizeAsync(
            parseResult.GetValue(package) ?? "", parseResult.GetValue(tokenInput) ?? "", parseResult.GetValue(tokenOutput),
            parseResult.GetValue(contextTokens), parseResult.GetValue(completionTokens), parseResult.GetValue(tokenLimit), parseResult.GetValue(workspace))));
        contract.Subcommands.Add(tokenize);

        var diff = new Command("diff", "Compara texto e IDs tokenizados de dois contratos versionados.");
        var expectedTokens = new Option<string>("--expected") { Description = "Snapshot tokenizado de referência.", Required = true };
        var actualTokens = new Option<string>("--actual") { Description = "Snapshot tokenizado atual.", Required = true };
        var diffOutput = new Option<string?>("--out") { Description = "Relatório JSON limitado ao workspace; padrão stdout." };
        diff.Options.Add(expectedTokens);
        diff.Options.Add(actualTokens);
        diff.Options.Add(diffOutput);
        diff.SetAction(parseResult => ExecuteAsync("contract.diff", () => ContractTokenCommand.DiffAsync(
            parseResult.GetValue(expectedTokens) ?? "", parseResult.GetValue(actualTokens) ?? "", parseResult.GetValue(diffOutput),
            parseResult.GetValue(workspace))));
        contract.Subcommands.Add(diff);
        return contract;
    }

    internal static void WriteJson<T>(T value) => Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static async Task<int> ExecuteAsync(string command, Func<Task<int>> action)
    {
        int exitCode;
        try
        {
            exitCode = await action().ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("error acesso recusado por política de privacidade/caminho.");
            exitCode = (int)ExitCode.PrivacyViolation;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException or ArgumentException)
        {
            Console.Error.WriteLine("error entrada inválida ou arquivo inacessível.");
            exitCode = (int)ExitCode.InvalidInput;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("error falha inesperada; detalhes sensíveis omitidos.");
            exitCode = (int)ExitCode.Failure;
        }
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + command.Replace('.', '-');
        Console.Error.WriteLine(JsonSerializer.Serialize(new { cmd = command, exit = exitCode, run_id = runId, @out = "-" }));
        return exitCode;
    }
}

internal enum ExitCode
{
    Success = 0,
    Failure = 1,
    Usage = 2,
    InvalidInput = 3,
    PackageInvalid = 4,
    ProviderUnavailable = 5,
    GateFailed = 6,
    Drift = 7,
    PrivacyViolation = 8,
    GpuBusy = 9,
    NativeCrash = 10,
    ParityMismatch = 11,
    Cancelled = 12,
}
