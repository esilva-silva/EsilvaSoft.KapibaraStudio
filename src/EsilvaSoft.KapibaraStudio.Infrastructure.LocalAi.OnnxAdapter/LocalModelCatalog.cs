using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Text.Json;
using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>Each immediate subfolder of the models directory is a candidate model. Validation is structural and never loads weights.</summary>
public sealed class LocalModelCatalog(string? defaultDirectory = null, IReadOnlyList<IModelAdapter>? adapters = null,
    ILocalModelFileAccess? fileAccess = null, ILocalWorkspacePaths? workspacePaths = null) : ILocalModelCatalog
{
    private const int MaximumCandidates = 100;
    // Kept in sync with the centrally pinned GenAI packages in Directory.Packages.props.
    private static readonly Version GenAiPackageVersion = new(0, 15, 2);
    private readonly ILocalModelFileAccess _fileAccess = fileAccess ?? throw new ArgumentNullException(nameof(fileAccess));
    private readonly IReadOnlyList<IModelAdapter> _adapters = adapters ?? ModelAdapters.CreateDefault(fileAccess ?? throw new ArgumentNullException(nameof(fileAccess)));
    private Func<string, string>? _localize;
    public string DefaultDirectory { get; } = defaultDirectory ?? workspacePaths?.GetModelsDirectory()
        ?? throw new ArgumentNullException(nameof(workspacePaths));

    public void SetLocalization(Func<string, string> localize) => _localize = localize ?? throw new ArgumentNullException(nameof(localize));
    private string L(string key, string fallback) => _localize?.Invoke(key) ?? fallback;
    private string F(string key, string fallback, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, L(key, fallback), arguments);

    public Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default) => DiscoverAsync(DefaultDirectory, cancellationToken);

    /// <summary>Invalid folders are returned with their state and never prevent the other models from being listed or loaded.</summary>
    public Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(string directory, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<LocalModelValidation>>(() =>
    {
        if (string.IsNullOrWhiteSpace(directory) || !_fileAccess.DirectoryExists(directory)) return [];
        return _fileAccess.EnumerateDirectories(directory)
            .Where(path => !Path.GetFileName(path).StartsWith('.'))
            .Order(StringComparer.OrdinalIgnoreCase).Take(MaximumCandidates)
            .Select(path => Validate(path, cancellationToken)).ToArray();
    }, cancellationToken);

    public Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Validate(path, cancellationToken), cancellationToken);

    private LocalModelValidation Validate(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !_fileAccess.DirectoryExists(path))
            return new(null, new(LocalModelState.NotInstalled, L("aiCatalogNotInstalled", "Modelo não instalado. Selecione um modelo do diretório ou uma pasta ONNX GenAI externa. Autocomplete básico ativo."))) { Path = path ?? "" };
        var root = _fileAccess.NormalizeDirectoryPath(path);
        var folder = _fileAccess.GetDirectoryName(root);
        LocalModelValidation Rejected(LocalModelState state, string message) => new(null, new(state, message) { ModelName = folder }) { Path = root };
        try
        {
            var configPath = Path.Combine(root, "genai_config.json");
            if (!_fileAccess.FileExists(configPath))
                return Rejected(LocalModelState.MissingFiles, L("aiCatalogMissingConfig", "Arquivos ausentes: genai_config.json. Selecione a pasta de uma exportação ONNX Runtime GenAI."));
            using var config = ReadModelJson(configPath);
            var model = config.RootElement.GetProperty("model");
            var type = model.GetProperty("type").GetString() ?? "";
            int? contextLength = model.TryGetProperty("context_length", out var contextValue)
                && contextValue.ValueKind == JsonValueKind.Number && contextValue.TryGetInt32(out var parsedContext)
                && parsedContext is >= 64 and <= 1_048_576 ? parsedContext : null;
            var decoder = model.GetProperty("decoder").GetProperty("filename").GetString();
            if (string.IsNullOrWhiteSpace(decoder) || Path.IsPathRooted(decoder)) throw new InvalidDataException();
            var decoderPath = Path.GetFullPath(Path.Combine(root, decoder));
            var containedRoot = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (!decoderPath.StartsWith(containedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException();
            var missing = new[] { decoderPath, Path.Combine(root, "tokenizer.json"), Path.Combine(root, "tokenizer_config.json") }
                .Where(file => !_fileAccess.FileExists(file) || _fileAccess.GetFileLength(file) == 0).Select(Path.GetFileName).ToArray();
            if (missing.Length > 0) return Rejected(LocalModelState.MissingFiles, F("aiCatalogMissingFiles", "Arquivos ausentes ou vazios: {0}.", string.Join(", ", missing)));
            var adapter = _adapters.FirstOrDefault(candidate => candidate.CanHandle(type, root));
            if (adapter is null)
                return Rejected(LocalModelState.Unsupported, F("aiCatalogUnsupportedArchitecture", "Arquitetura \"{0}\" não suportada. Suportadas: {1}.", type[..Math.Min(type.Length, 64)], string.Join("; ", _adapters.Select(a => a.Description))));
            LocalModelMetadata? metadata;
            try { metadata = LocalModelMetadataReader.Read(root, _fileAccess); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or FormatException)
            {
                return Rejected(LocalModelState.Invalid, F("aiCatalogMetadataInvalid", "{0} inválido: confira nomes, tipos e limites dos campos.", LocalModelMetadata.FileName));
            }
            // Compatibilidade de contrato de prompt: decidida só com o metadata já em memória, sem I/O extra e sem
            // carregar pesos. Ausência de declaração nunca é incompatibilidade (pacotes antigos continuam válidos).
            if (metadata?.ContextContract is { } contract && !LocalModelContextContracts.IsSupported(contract))
                return Rejected(LocalModelState.Invalid,
                    F("aiCatalogContextContract", "Modelo requer contrato de contexto \"{0}\", não suportado por esta versão do aplicativo. Contratos suportados: {1}.", contract, string.Join(", ", LocalModelContextContracts.Supported)));
            if (metadata?.MinimumGenAi is { } minimum && minimum > GenAiPackageVersion)
                return Rejected(LocalModelState.Unsupported,
                    F("aiCatalogMinimumGenAi", "Modelo requer ONNX Runtime GenAI {0}; esta versão inclui {1}.", minimum, GenAiPackageVersion));
            if (metadata?.Capabilities is { } flags &&
                (flags.HasFlag(LocalModelCapabilities.Agent) || flags.HasFlag(LocalModelCapabilities.Tools)
                    || flags.HasFlag(LocalModelCapabilities.Reasoning)) && type != "qwen3")
                return Rejected(LocalModelState.Unsupported, "Capacidades de agente local exigem pacote qwen3 compatível.");
            using var tokenizer = ReadModelJson(Path.Combine(root, "tokenizer.json"));
            using (ReadModelJson(Path.Combine(root, "tokenizer_config.json"))) { }
            if (adapter.Validate(new(root, type, decoderPath, tokenizer.RootElement, metadata)) is { } failure) return Rejected(failure.State, failure.Message);
            token.ThrowIfCancellationRequested();
            var name = string.IsNullOrWhiteSpace(metadata?.Name) ? folder : metadata.Name;
            var definition = new LocalModelDefinition(folder, name, root, adapter.Architecture, Path.Combine(root, "tokenizer.json"))
            {
                PromptFormat = adapter.PromptFormat, Capabilities = metadata?.Capabilities ?? adapter.DefaultCapabilities, Metadata = metadata,
                ContextLength = contextLength, AutocompleteMaximumTokens = metadata?.Autocomplete.MaximumTokens,
                ModelSizeBytes = _fileAccess.EnumerateTopLevelFiles(root)
                    .Where(file => file.Path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith(".data", StringComparison.OrdinalIgnoreCase))
                    .Sum(file => file.Length)
            };
            return new(definition, new(LocalModelState.Available, L("aiCatalogAvailable", "Arquivos encontrados. Use Testar modelo para validar inferência e provider.")) { ModelName = name }) { Path = root };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return Rejected(LocalModelState.Invalid, L("aiCatalogInvalid", "Modelo inválido: confira genai_config.json, decoder ONNX, tokenizer.json e tokenizer_config.json."));
        }
    }

    internal static JsonDocument ReadJson(string path, ILocalModelFileAccess fileAccess, long maximumBytes = 32 * 1024 * 1024)
    {
        if (fileAccess.GetFileLength(path) > maximumBytes) throw new InvalidDataException();
        using var stream = fileAccess.OpenRead(path);
        return JsonDocument.Parse(stream);
    }

    private JsonDocument ReadModelJson(string path, long maximumBytes = 32 * 1024 * 1024)
    {
        return ReadJson(path, _fileAccess, maximumBytes);
    }

}
