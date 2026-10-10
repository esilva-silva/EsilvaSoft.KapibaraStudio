using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class ContractTokenCommand
{
    private const int MaximumRecords = 20_000;
    private const int MaximumLines = 100_000;
    private const int MaximumTokenIdsPerRecord = 1_048_576;
    private const int MaximumOutputBytes = 128 * 1024 * 1024;
    private const long MaximumInputBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<int> TokenizeAsync(string package, string input, string? output, int contextTokens, int completionTokens,
        int? limit, string? workspaceOption)
    {
        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null) return Usage("informe --workspace ou KAPILAB_WORKSPACE.");
        if (limit is < 1 or > MaximumRecords) return Usage($"--limit deve estar entre 1 e {MaximumRecords}.");
        if (contextTokens is < 64 or > 1_048_576) return Usage("--context-tokens deve estar entre 64 e 1.048.576.");
        if (completionTokens is < 1 or > 2048) return Usage("--completion-tokens deve estar entre 1 e 2.048.");

        var fileAccess = new KapiLabModelFileAccess();
        var packagePath = Path.GetFullPath(package);
        var inputPath = Path.GetFullPath(input, workspace);
        LabWorkspace.RefuseBlindInput(packagePath, workspace);
        LabWorkspace.RefuseBlindInput(inputPath, workspace);
        var catalog = new LocalModelCatalog(Path.GetDirectoryName(packagePath), fileAccess: fileAccess);
        var validation = await catalog.ValidateAsync(packagePath).ConfigureAwait(false);
        if (validation.Status.State != LocalModelState.Available || validation.Model is null)
        {
            KapiLabCommandLine.WriteJson(ModelInspection.From(validation, includeHardware: false));
            return (int)ExitCode.PackageInvalid;
        }

        var model = validation.Model;
        ITokenizer? tokenizer = null;
        Model? nativeModel = null;
        try
        {
            var adapters = ModelAdapters.CreateDefault(fileAccess);
            var adapter = ModelAdapters.For(model, adapters);
            using (var config = new Config(model.Path))
            {
                config.ClearProviders();
                nativeModel = new Model(config);
            }
            tokenizer = adapter.CreateTokenizer(nativeModel, model.Path);
            var promptBuilder = adapter.CreatePromptBuilder();
            var identity = new TokenizerPackageIdentity(model.Architecture, model.PromptFormat,
                ContractTokenDiff.Sha256(Path.Combine(model.Path, "genai_config.json")),
                ContractTokenDiff.Sha256(Path.Combine(model.Path, "tokenizer.json")),
                ContractTokenDiff.Sha256(Path.Combine(model.Path, "tokenizer_config.json")),
                File.Exists(Path.Combine(model.Path, DeepSeekCoderModelAdapter.ManifestFileName))
                    ? ContractTokenDiff.Sha256(Path.Combine(model.Path, DeepSeekCoderModelAdapter.ManifestFileName)) : "");
            var lines = new List<string>();
            var effectiveCompletionTokens = Math.Min(completionTokens, model.EffectiveAutocompleteMaximumTokens);
            var effectiveContextTokens = ContractTokenBudget.EffectiveContextTokens(contextTokens, completionTokens,
                model.EffectiveContextLength, model.EffectiveAutocompleteMaximumTokens);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            var outputBytes = 0;
            var lineNumber = 0;
            await using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumInputBytes) throw new InvalidDataException("Arquivo de entrada excede o limite de 2 GiB.");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            await foreach (var line in ContractRenderCommand.ReadBoundedLinesAsync(reader).ConfigureAwait(false))
            {
                lineNumber++;
                if (lineNumber > MaximumLines) throw new InvalidDataException("Arquivo excede o limite de 100.000 linhas.");
                if (line.Length == 0) continue;
                if (count >= (limit ?? MaximumRecords))
                {
                    if (limit is null) throw new InvalidDataException("Arquivo excede o limite; use --limit para selecionar prefixo explícito.");
                    break;
                }
                var rendered = JsonSerializer.Deserialize<RenderedContract>(line, JsonOptions)
                    ?? throw new InvalidDataException("Registro renderizado inválido.");
                if (rendered.Schema != "kapilab-render-v1" || rendered.Contract != "editor-context-v1"
                    || string.IsNullOrWhiteSpace(rendered.Id) || rendered.Id.Length > 256
                    || rendered.ModelPrefix is null || rendered.Suffix is null
                    || !IsSha256(rendered.ModelPrefixSha256) || !IsSha256(rendered.SuffixSha256))
                    throw new InvalidDataException("Registro não corresponde a kapilab-render-v1/editor-context-v1.");
                if (!ids.Add(rendered.Id)) throw new InvalidDataException("Entrada contém id duplicado.");
                if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(rendered.ModelPrefix)
                    || CompletionOutputProcessor.ContainsReservedOrSensitiveText(rendered.Suffix))
                    throw new UnauthorizedAccessException("Contrato contém marcador reservado ou texto sensível.");
                if (!HashMatches(rendered.ModelPrefix, rendered.ModelPrefixSha256)
                    || !HashMatches(rendered.Suffix, rendered.SuffixSha256))
                    throw new InvalidDataException("Hashes de texto não correspondem ao contrato renderizado.");

                var tokens = promptBuilder.Build(rendered.ModelPrefix, rendered.Suffix, effectiveContextTokens, tokenizer);
                if (tokens.Count > MaximumTokenIdsPerRecord || tokens.Any(token => token < 0))
                    throw new InvalidDataException("Builder retornou lista de token IDs inválida ou acima do limite.");
                var serialized = JsonSerializer.Serialize(new TokenizedContractRecord("kapilab-tokenized-contract-v2", rendered.Id,
                    rendered.Contract, rendered.ModelPrefixSha256, rendered.SuffixSha256, effectiveContextTokens, identity, tokens,
                    contextTokens, effectiveCompletionTokens), JsonOptions);
                outputBytes = checked(outputBytes + Encoding.UTF8.GetByteCount(serialized) + 1);
                if (outputBytes > MaximumOutputBytes) throw new InvalidDataException("Saída excede o limite de 128 MiB.");
                lines.Add(serialized);
                count++;
            }

            if (count == 0) throw new InvalidDataException("Contrato renderizado sem registros.");
            await WriteOutputAsync(lines, output, workspace, [inputPath]).ConfigureAwait(false);
            Console.Error.WriteLine(JsonSerializer.Serialize(new { schema = "kapilab-contract-tokenize-summary-v1", records = count,
                family = identity.Family, promptFormat = identity.PromptFormat,
                requestedContextTokens = contextTokens, effectiveContextTokens, completionTokens = effectiveCompletionTokens,
                note = "tokenizer real do adapter; Model GenAI inicializado explicitamente para tokenização" }, JsonOptions));
            return (int)ExitCode.Success;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("error conteúdo recusado pela política de privacidade.");
            return (int)ExitCode.PrivacyViolation;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine("error pacote, contrato ou tokenizer incompatível; detalhes sensíveis omitidos.");
            return exception is NotSupportedException ? (int)ExitCode.PackageInvalid : (int)ExitCode.InvalidInput;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("error falha ao inicializar tokenizer/modelo GenAI.");
            return (int)ExitCode.ProviderUnavailable;
        }
        finally
        {
            (tokenizer as IDisposable)?.Dispose();
            nativeModel?.Dispose();
        }
    }

    public static async Task<int> DiffAsync(string expected, string actual, string? output, string? workspaceOption)
    {
        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null) return Usage("informe --workspace ou KAPILAB_WORKSPACE.");
        try
        {
            var left = await ReadRecordsAsync(expected, workspace).ConfigureAwait(false);
            var right = await ReadRecordsAsync(actual, workspace).ConfigureAwait(false);
            var result = ContractTokenDiff.Compare(left, right);
            var report = new { schema = "kapilab-contract-diff-v1", result.Matches, result.ExpectedRecords,
                result.ActualRecords, result.TokenizerIdentityMatches, result.Complete, differences = result.Differences };
            var serialized = JsonSerializer.Serialize(report, JsonOptions);
            if (string.IsNullOrWhiteSpace(output) || output == "-") Console.Out.WriteLine(serialized);
            else await WriteOutputAsync([serialized], output, workspace,
                [Path.GetFullPath(expected, workspace), Path.GetFullPath(actual, workspace)]).ConfigureAwait(false);
            return result.Matches ? (int)ExitCode.Success : 11;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("error snapshots de tokenização inválidos ou inacessíveis.");
            return exception is UnauthorizedAccessException ? (int)ExitCode.PrivacyViolation : (int)ExitCode.InvalidInput;
        }
    }

    private static async Task<List<TokenizedContractRecord>> ReadRecordsAsync(string path, string workspace)
    {
        var fullPath = Path.GetFullPath(path, workspace);
        LabWorkspace.RefuseBlindInput(fullPath, workspace);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumInputBytes) throw new InvalidDataException("Arquivo excede o limite de 2 GiB.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
        var records = new List<TokenizedContractRecord>();
        var lineNumber = 0;
        await foreach (var line in ContractRenderCommand.ReadBoundedLinesAsync(reader).ConfigureAwait(false))
        {
            lineNumber++;
            if (lineNumber > MaximumLines) throw new InvalidDataException("Arquivo excede o limite de 100.000 linhas.");
            if (line.Length == 0) continue;
            if (records.Count == MaximumRecords) throw new InvalidDataException("Snapshot excede o limite de registros.");
            var record = JsonSerializer.Deserialize<TokenizedContractRecord>(line, JsonOptions);
            if (record?.Schema is not ("kapilab-tokenized-contract-v1" or "kapilab-tokenized-contract-v2") || string.IsNullOrWhiteSpace(record.Id)
                || record.Tokenizer is null || record.TokenIds is null || record.TokenIds.Count > MaximumTokenIdsPerRecord
                || record.RequestedContextTokens is < 64 or > 1_048_576 || record.CompletionTokens is < 1 or > 2048
                || (record.Schema == "kapilab-tokenized-contract-v2" && (record.RequestedContextTokens is null || record.CompletionTokens is null))
                || record.TokenIds.Any(token => token < 0) || !IsSha256(record.ModelPrefixSha256) || !IsSha256(record.SuffixSha256))
                throw new InvalidDataException("Snapshot de tokens inválido.");
            records.Add(record);
        }
        return records;
    }

    private static async Task WriteOutputAsync(IReadOnlyList<string> lines, string? output, string workspace,
        IReadOnlyList<string>? inputPaths = null)
    {
        if (string.IsNullOrWhiteSpace(output) || output == "-")
        {
            foreach (var line in lines) await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
            return;
        }
        var destination = LabWorkspace.ResolveOutput(workspace, output);
        if (inputPaths is not null) LabWorkspace.RefuseInputOverwrite(workspace, destination, inputPaths);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllLinesAsync(temporary, lines, new UTF8Encoding(false)).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine("error " + message);
        return (int)ExitCode.Usage;
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool HashMatches(string value, string expected) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .Equals(expected, StringComparison.OrdinalIgnoreCase);

    private sealed record RenderedContract(string Schema, string Id, string Contract, string ModelPrefix, string Suffix,
        string ModelPrefixSha256, string SuffixSha256);
}
