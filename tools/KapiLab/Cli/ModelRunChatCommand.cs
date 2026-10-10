using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

/// <summary>One independent user turn per record, through the IDE's chat model service.</summary>
internal static class ModelRunChatCommand
{
    internal const string InputSchema = "kapilab-chat-input-v1";
    private const string EventSchema = "kapilab-chat-event-v1";
    internal const int MaximumRecords = 100;
    private const int MaximumMessageCharacters = 8192;
    private const int MaximumOutputCharacters = 65_536;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal sealed record ChatInput(string Id, string Message);

    internal static ChatInput ParseLine(string line)
    {
        if (line.Length is < 1 or > 16_384) throw new InvalidDataException("Linha de chat fora do limite.");
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Registro de chat inválido.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
            if (!names.Add(field.Name) || field.Name is not ("schema" or "id" or "message"))
                throw new InvalidDataException("Campo de chat desconhecido ou duplicado.");
        if (names.Count != 3 || root.GetProperty("schema").ValueKind != JsonValueKind.String ||
            root.GetProperty("schema").GetString() != InputSchema ||
            root.GetProperty("id").ValueKind != JsonValueKind.String ||
            root.GetProperty("message").ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Contrato de chat ausente ou não suportado.");
        var id = root.GetProperty("id").GetString()!;
        var message = root.GetProperty("message").GetString()!;
        if (id.Length is < 1 or > 128 || !id.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new InvalidDataException("ID de chat inválido.");
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaximumMessageCharacters || message.Contains('\0'))
            throw new InvalidDataException("Mensagem de chat ausente ou acima do limite.");
        if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(message))
            throw new UnauthorizedAccessException("Mensagem contém marcador reservado ou texto sensível.");
        return new(id, message);
    }

    internal static async Task<IReadOnlyList<ChatInput>> ReadInputAsync(string path, int? limit)
    {
        var records = new List<ChatInput>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is < 1 or > 2 * 1024 * 1024) throw new InvalidDataException("Entrada de chat vazia ou acima de 2 MiB.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        await foreach (var line in ContractRenderCommand.ReadBoundedLinesAsync(reader).ConfigureAwait(false))
        {
            if (line.Length == 0) continue;
            if (records.Count == MaximumRecords) throw new InvalidDataException("Entrada de chat excede 100 registros.");
            var record = ParseLine(line);
            if (!ids.Add(record.Id)) throw new InvalidDataException("Entrada de chat contém ID duplicado.");
            records.Add(record);
        }
        if (records.Count == 0) throw new InvalidDataException("Entrada de chat sem registros.");
        return limit is null ? records : records.Take(limit.Value).ToArray();
    }

    public static async Task<int> RunAsync(string package, string input, string? workspaceOption, string device,
        int maximumTokens, int? limit, bool viaQueue = false, bool standaloneGpu = false, int? contextTokens = null)
    {
        if (maximumTokens is < 1 or > 4096 || limit is < 1 or > MaximumRecords || !IsContextTokenRequestValid(contextTokens))
        {
            Console.Error.WriteLine("error --max-tokens deve estar entre 1 e 4096, --context-tokens entre 64 e o máximo absoluto e --limit entre 1 e 100.");
            return (int)ExitCode.Usage;
        }
        if (!ModelRunCommand.TryParseDeviceOption(device, out var acceleration))
        {
            Console.Error.WriteLine("error --device aceita apenas auto, cpu, gpu ou npu.");
            return (int)ExitCode.Usage;
        }
        if (!GpuExecutionCoordination.TryParseMode(viaQueue, standaloneGpu, out var gpuMode))
        { Console.Error.WriteLine("error escolha apenas um modo GPU."); return (int)ExitCode.Usage; }
        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null)
        {
            Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE para selecionar o laboratório.");
            return (int)ExitCode.Usage;
        }
        var packagePath = Path.GetFullPath(package);
        var inputPath = Path.GetFullPath(input, workspace);
        LabWorkspace.RefuseBlindInput(packagePath, workspace);
        LabWorkspace.RefuseBlindInput(inputPath, workspace);
        var records = await ReadInputAsync(inputPath, limit).ConfigureAwait(false);
        var fileAccess = new KapiLabModelFileAccess();
        var catalog = new LocalModelCatalog(Path.GetDirectoryName(packagePath), fileAccess: fileAccess);
        var validation = await catalog.ValidateAsync(packagePath).ConfigureAwait(false);
        if (validation.Status.State != LocalModelState.Available || validation.Model is not { } model ||
            !model.Capabilities.HasFlag(LocalModelCapabilities.Chat) || model.PromptFormat != LocalModelPromptFormats.Qwen3ChatMlTools)
        {
            Console.Error.WriteLine("error pacote sem chat Qwen3 compatível com o formatter da IDE.");
            return (int)ExitCode.PackageInvalid;
        }
        var settings = new AutocompleteSettings
        {
            ModelPath = packagePath,
            Acceleration = acceleration,
            ContextTokens = ResolveContextTokens(model, contextTokens),
            MaximumCompletionTokens = maximumTokens,
        }.Validate();
        await using var models = KapiLabModelServices.Create(catalog, fileAccess, "model-run-chat", workspace, gpuMode);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return await RunRecordsAsync(models, model, settings, records, Console.Out, cancellation.Token).ConfigureAwait(false); }
        finally { Console.CancelKeyPress -= handler; }
    }

    internal static int ResolveContextTokens(LocalModelDefinition model, int? requestedContextTokens) =>
        Math.Min(requestedContextTokens ?? AutocompleteSettings.AbsoluteContextMaximum,
            Math.Min(model.EffectiveContextLength, AutocompleteSettings.AbsoluteContextMaximum));

    internal static bool IsContextTokenRequestValid(int? requestedContextTokens) =>
        requestedContextTokens is null or (>= 64 and <= AutocompleteSettings.AbsoluteContextMaximum);

    internal static async Task<int> RunRecordsAsync(ILocalAiModelService models, LocalModelDefinition model,
        AutocompleteSettings settings, IReadOnlyList<ChatInput> records, TextWriter output, CancellationToken cancellationToken = default)
    {
        foreach (var record in records)
        {
            var chunks = new List<GeneratedChunk>();
            var response = new StringBuilder();
            try
            {
                var prompt = new ModelChatPrompt([new ModelChatMessage("user", record.Message)],
                    AddGenerationPrompt: true);
                await foreach (var chunk in models.StreamAsync(LocalModelRole.Chat, settings,
                    _ => new ModelGenerationRequest("", "", settings.ContextTokens, settings.MaximumCompletionTokens, RequireFullContext: true)
                    { ChatPrompt = prompt }, AiRequestPriority.Interactive, AiModelLoadPolicy.LoadIfNeeded, cancellationToken)
                    .WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    chunks.Add(chunk);
                    response.Append(chunk.Text);
                    if (response.Length > MaximumOutputCharacters)
                        throw new InvalidDataException("Resposta de chat excede o limite em memória.");
                }
                if (chunks.Count == 0 || !chunks[^1].IsFinal)
                    throw new InvalidDataException("Runtime encerrou o chat sem chunk final.");
                if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(response.ToString()))
                {
                    await WriteEventAsync(output, new ChatEvent(EventSchema, record.Id, "rejected", null, null, null,
                        null, null, null, null, null, null, null, null, "privacy_output")).ConfigureAwait(false);
                    return (int)ExitCode.PrivacyViolation;
                }
                for (var index = 0; index < chunks.Count; index++)
                {
                    var chunk = chunks[index];
                    await WriteEventAsync(output, new ChatEvent(EventSchema, record.Id, chunk.IsFinal ? "final" : "chunk",
                        index, chunk.Text, chunk.TokenId, chunk.GeneratedTokens,
                        chunk.IsFinal ? chunk.IsComplete : null,
                        chunk.IsFinal ? chunk.Elapsed.TotalMilliseconds : null,
                        chunk.IsFinal ? chunk.TimeToFirstToken?.TotalMilliseconds : null,
                        chunk.IsFinal ? chunk.Provider : null,
                        chunk.IsFinal ? chunk.UsedCpuFallback : null,
                        settings.ContextTokens, settings.MaximumCompletionTokens, null)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                await WriteEventAsync(output, Error(record.Id, "cancelled")).ConfigureAwait(false);
                return (int)ExitCode.Cancelled;
            }
            catch (LocalModelUnavailableException exception)
            {
                if (KapiLabModelServices.IsGpuLockUnavailable(exception))
                {
                    await WriteEventAsync(output, Error(record.Id, "gpu_lock_busy")).ConfigureAwait(false);
                    return (int)ExitCode.GpuBusy;
                }
                await WriteEventAsync(output, Error(record.Id, exception.UnavailableReason.ToString())).ConfigureAwait(false);
                if (exception.UnavailableReason == LocalModelUnavailableReason.ContextOverflow)
                    return (int)ExitCode.InvalidInput;
                return exception.UnavailableReason is LocalModelUnavailableReason.ModelInvalid or
                    LocalModelUnavailableReason.CapabilityMissing or LocalModelUnavailableReason.NoModelConfigured
                    ? (int)ExitCode.PackageInvalid : (int)ExitCode.ProviderUnavailable;
            }
            catch (LocalModelContextException)
            {
                await WriteEventAsync(output, Error(record.Id, "context_overflow")).ConfigureAwait(false);
                return (int)ExitCode.InvalidInput;
            }
            catch (NotSupportedException)
            {
                await WriteEventAsync(output, Error(record.Id, "chat_formatter_unavailable")).ConfigureAwait(false);
                return (int)ExitCode.PackageInvalid;
            }
            catch (InvalidDataException)
            {
                await WriteEventAsync(output, Error(record.Id, "invalid_or_oversized_output")).ConfigureAwait(false);
                return (int)ExitCode.Failure;
            }
            catch (Exception)
            {
                await WriteEventAsync(output, Error(record.Id, "runtime_failure")).ConfigureAwait(false);
                return (int)ExitCode.ProviderUnavailable;
            }
        }
        return (int)ExitCode.Success;
    }

    private static ChatEvent Error(string id, string error) => new(EventSchema, id, "error", null, null, null,
        null, null, null, null, null, null, null, null, error);

    private static Task WriteEventAsync(TextWriter output, ChatEvent value) =>
        output.WriteLineAsync(JsonSerializer.Serialize(value, JsonOptions));

    private sealed record ChatEvent(string Schema, string Id, string Kind, int? Sequence, string? Text, int? TokenId,
        int? GeneratedTokens, bool? IsComplete, double? ElapsedMilliseconds, double? TimeToFirstTokenMilliseconds,
        string? Provider, bool? UsedCpuFallback, int? ContextWindowTokens, int? MaximumCompletionTokens, string? Error);
}
