using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

/// <summary>Captures the legacy local chat contract through LocalAgentProvider, without loading a model.</summary>
internal static class LegacyChatRenderCommand
{
    internal const string Contract = "local-agent-fim-v0";
    internal const string InputSchema = "kapilab-legacy-chat-input-v1";
    internal const string OutputSchema = "kapilab-legacy-chat-render-v1";
    private const int MaximumRecords = 20_000;
    private const int MaximumInputBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal sealed record InputRecord(string Id, string TabId, long DocumentVersion, string UserMessage,
        string? SelectedText, string? EditorText);

    internal sealed record CapturedRequest(string Prefix, string Suffix, int ContextTokens, int MaximumTokens,
        bool RequireFullContext, double Temperature);

    internal sealed record CapturedContractRequest(CapturedRequest Request, string? AuthorizedContext);

    internal sealed record RenderedRecord(string Schema, string Id, string Contract, string RequestPrefixSha256,
        string RequestSuffixSha256, int RequestPrefixCharacters, int RequestSuffixCharacters, int ContextTokens,
        int MaximumTokens, bool RequireFullContext, double Temperature, string? AuthorizedContextSha256,
        int? AuthorizedContextCharacters);

    internal static async Task<int> RunAsync(string input, string? output, int? limit, string? ids,
        bool noEditorContext, string? workspaceOption)
    {
        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null) return Usage("informe --workspace ou KAPILAB_WORKSPACE.");
        if (limit is < 1 or > MaximumRecords) return Usage($"--limit deve estar entre 1 e {MaximumRecords}.");

        try
        {
            var inputPath = KapiLabCommandLine.ReadArtifactPath(input, workspace);
            if (new FileInfo(inputPath).Length is < 1 or > MaximumInputBytes)
                throw new InvalidDataException("Arquivo de entrada vazio ou acima de 4 MiB.");
            var requestedIds = string.IsNullOrWhiteSpace(ids)
                ? null
                : ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.Ordinal);
            if (requestedIds is { Count: 0 }) throw new InvalidDataException("--ids não contém identificadores válidos.");

            var records = await ReadInputsAsync(inputPath, limit, requestedIds).ConfigureAwait(false);
            if (records.Count == 0) throw new InvalidDataException("Entrada sem registros selecionados.");
            var captured = new List<(InputRecord Input, CapturedContractRequest Capture)>();
            foreach (var record in records)
            {
                captured.Add((record, await CaptureAsync(record, noEditorContext).ConfigureAwait(false)));
            }

            var rendered = captured.Select(item => ToRendered(item.Input, item.Capture.Request, item.Capture.AuthorizedContext)).ToArray();
            var lines = rendered.Select(record => JsonSerializer.Serialize(record, JsonOptions)).ToArray();
            if (string.IsNullOrWhiteSpace(output) || output == "-")
            {
                foreach (var line in lines) await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
            }
            else
            {
                var destination = LabWorkspace.ResolveOutput(workspace, output);
                LabWorkspace.RefuseInputOverwrite(workspace, destination, [inputPath]);
                await WriteAtomicallyAsync(destination, lines).ConfigureAwait(false);
            }

            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "kapilab-legacy-chat-render-summary-v1",
                records = lines.Length,
                contract = Contract,
                textIncluded = false,
                output = output ?? "-",
            }, JsonOptions));
            return (int)ExitCode.Success;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("error conteúdo recusado por política de privacidade/caminho; nenhum conteúdo foi emitido.");
            return (int)ExitCode.PrivacyViolation;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or ArgumentException or OverflowException)
        {
            Console.Error.WriteLine("error entrada de contrato legado inválida ou inacessível; nenhum conteúdo foi emitido.");
            return (int)ExitCode.InvalidInput;
        }
    }

    internal static InputRecord ParseLine(string line)
    {
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8, CommentHandling = JsonCommentHandling.Disallow });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Registro legado precisa ser objeto JSON.");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!fields.Add(property.Name)) throw new InvalidDataException("Registro contém propriedade duplicada.");
            if (property.Name is not ("schema" or "id" or "tabId" or "documentVersion" or "userMessage" or "selectedText" or "editorText"))
                throw new InvalidDataException("Registro contém propriedade desconhecida.");
        }
        if (fields.Count is < 5 or > 7 || !TryString(root, "schema", out var schema) || schema != InputSchema ||
            !TryString(root, "id", out var id) || !IsSafeId(id) ||
            !TryString(root, "tabId", out var tabId) || !IsSafeId(tabId) ||
            !root.TryGetProperty("documentVersion", out var versionElement) || !versionElement.TryGetInt64(out var version) || version < 0 ||
            !TryString(root, "userMessage", out var userMessage) || string.IsNullOrWhiteSpace(userMessage) || userMessage.Length > 8192)
            throw new InvalidDataException("Registro legado inválido; são necessários schema, id, tabId, documentVersion e userMessage.");

        var selected = OptionalString(root, "selectedText", 32 * 1024);
        var editor = OptionalString(root, "editorText", 32 * 1024);
        if ((selected?.Length ?? 0) + (editor?.Length ?? 0) > AgentContextProvider.MaximumSharedTextChars)
            throw new InvalidDataException("Texto de contexto excede 32 Ki caracteres.");
        if (userMessage.Contains('\0')) throw new InvalidDataException("Mensagem contém caractere NUL.");
        return new(id, tabId, version, userMessage, selected, editor);
    }

    internal static async Task<CapturedContractRequest> CaptureAsync(InputRecord input, bool noEditorContext = false)
    {
        var modelPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "kapilab-recorder", "synthetic-model"));
        var model = new LocalModelDefinition("kapilab-recorder", "KapiLab recorder fixture", modelPath, "QwenCoder");
        var settings = new AutocompleteSettings { ModelPath = modelPath, Mode = AutocompleteMode.Ai, ChatEnabled = true };
        var models = new RecordingModelService(model);
        var autocomplete = new RecordingAutocompleteService(settings);
        var provider = new LocalAgentProvider(models, autocomplete);
        var contextProvider = new AgentContextProvider(new EmptyConnectionProfileRepository());
        var snapshot = await contextProvider.CaptureAsync(new AgentContextCaptureRequest(input.TabId, input.DocumentVersion,
            SelectedText: noEditorContext ? null : input.SelectedText,
            EditorText: noEditorContext ? null : input.EditorText), CancellationToken.None).ConfigureAwait(false);
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(LocalAgentProvider.Id), CancellationToken.None)
            .ConfigureAwait(false);
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(snapshot.ToTurnRequest(AgentTurnId.New(), input.UserMessage), CancellationToken.None)
            .ConfigureAwait(false))
        {
            events.Add(item);
        }
        if (events.Any(item => item.Kind == AgentEventKind.AgentError))
        {
            if (events.Any(item => item.Text == "ContextRejected"))
                throw new UnauthorizedAccessException("Contexto de chat recusado pela política de privacidade.");
            throw new InvalidDataException("Provider local não produziu pedido de geração para o fixture.");
        }
        var capturedRequest = models.CapturedRequest ?? throw new InvalidDataException("Provider não chamou a fábrica do pedido de modelo.");
        return new(capturedRequest, snapshot.AuthorizedContext);
    }

    private static async Task<List<InputRecord>> ReadInputsAsync(string path, int? limit, HashSet<string>? requestedIds)
    {
        var records = new List<InputRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        await foreach (var line in ContractRenderCommand.ReadBoundedLinesAsync(reader).ConfigureAwait(false))
        {
            if (line.Length == 0) continue;
            var record = ParseLine(line);
            if (!seen.Add(record.Id)) throw new InvalidDataException("Entrada contém IDs duplicados.");
            if (requestedIds is null || requestedIds.Contains(record.Id)) records.Add(record);
            if (records.Count > (limit ?? MaximumRecords)) throw new InvalidDataException("Entrada excede --limit; use --limit para selecionar um prefixo.");
            if (seen.Count > MaximumRecords) throw new InvalidDataException("Entrada excede 20.000 registros.");
        }
        if (requestedIds is not null && requestedIds.Except(seen, StringComparer.Ordinal).Any())
            throw new InvalidDataException("Um ou mais IDs solicitados não foram encontrados.");
        return records;
    }

    internal static RenderedRecord ToRendered(InputRecord input, CapturedRequest request, string? context)
    {
        static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return new(OutputSchema, input.Id, Contract, Hash(request.Prefix), Hash(request.Suffix), request.Prefix.Length,
            request.Suffix.Length, request.ContextTokens, request.MaximumTokens, request.RequireFullContext, request.Temperature,
            context is null ? null : Hash(context), context?.Length);
    }

    private static string? OptionalString(JsonElement root, string name, int maximumLength)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"Campo {name} precisa ser texto.");
        var text = value.GetString() ?? "";
        if (text.Length > maximumLength || text.Contains('\0')) throw new InvalidDataException($"Campo {name} excede o limite.");
        return text;
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.String) return false;
        value = item.GetString() ?? "";
        return true;
    }

    private static bool IsSafeId(string value) => value.Length is > 0 and <= 128 &&
        value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static async Task WriteAtomicallyAsync(string path, IReadOnlyList<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                foreach (var line in lines) await writer.WriteLineAsync(line).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine("error " + message);
        return (int)ExitCode.Usage;
    }

    private sealed class EmptyConnectionProfileRepository : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>([]);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAutocompleteService(AutocompleteSettings settings) : IAutocompleteService
    {
        public AutocompleteSettings Settings { get; } = settings;
        public LocalModelStatus Status { get; } = new(LocalModelState.Available, "fixture");
        public event EventHandler? SettingsChanged { add { } remove { } }
        public Task ConfigureAsync(AutocompleteSettings value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<AutocompleteResult?>(null);
        public Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
    }

    private sealed class RecordingModelService(LocalModelDefinition model) : ILocalAiModelService
    {
        public string DefaultDirectory => Path.GetDirectoryName(model.Path)!;
        public LocalModelStatus Status { get; } = new(LocalModelState.Available, "fixture");
        public LocalModelDefinition? LoadedModel => model;
        public CapturedRequest? CapturedRequest { get; private set; }
        public event EventHandler? StatusChanged { add { } remove { } }
        public Task<IReadOnlyList<LocalModelValidation>> DiscoverModelsAsync(string? directory = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalModelValidation>>([]);
        public Task<LocalModelValidation> ValidateModelAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(PathEquals(path, model.Path)
                ? new LocalModelValidation(model, new LocalModelStatus(LocalModelState.Available, "fixture"))
                : new LocalModelValidation(null, new LocalModelStatus(LocalModelState.NotInstalled, "fixture")));
        public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiHardwareDevice>>([]);
        public LocalModelCapabilities GetCapabilities() => model.Capabilities;
        public Task<LocalModelDefinition> LoadModelAsync(LocalModelRole role, AutocompleteSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(model);
        public Task UnloadModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SwitchModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings,
            Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
            AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The recorder captures StreamAsync only; inference is never performed.");
        public async IAsyncEnumerable<GeneratedChunk> StreamAsync(LocalModelRole role, AutocompleteSettings settings,
            Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
            AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturedRequest = ToCaptured(request(model));
            await Task.Yield();
            yield return new GeneratedChunk("", 0, true);
        }
        public void CancelGeneration() { }
        public Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelTestReport(false, "recorder fixture; no inference", []));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static CapturedRequest ToCaptured(ModelGenerationRequest request) => new(request.Prefix, request.Suffix,
            request.ContextTokens, request.MaximumTokens, request.RequireFullContext, request.Temperature);

        private static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
