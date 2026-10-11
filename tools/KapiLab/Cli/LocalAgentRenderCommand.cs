using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class LocalAgentRenderCommand
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    internal sealed record InputRecord(string Schema, string Id, bool Synthetic, LocalAgentPromptInput Input,
        bool AddGenerationPrompt = true, string DisabledReasoningSuffix = Qwen3ChatMlFallbackRenderer.DisabledReasoningSuffix);
    internal sealed record RenderedRecord(string Schema, string Id, string Contract, string ModelPrefixSha256,
        string SuffixSha256, string? ModelPrefix, string? Suffix, InputRecord? AgentInput);
    private static readonly Lazy<HashSet<string>> SyntheticFixtureHashes = new(() =>
    {
        using var stream = typeof(LocalAgentRenderCommand).Assembly.GetManifestResourceStream("local-agent-qwen3-v1.turns.jsonl")
            ?? throw new InvalidDataException();
        using var reader = new StreamReader(stream);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
            hashes.Add(Hash(JsonSerializer.Serialize(JsonSerializer.Deserialize<InputRecord>(line, JsonOptions), JsonOptions)));
        return hashes;
    });

    internal static InputRecord ParseLine(string line)
    {
        using var json = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32 });
        JsonContractValidation.RequireUniqueProperties(json.RootElement);
        var record = JsonSerializer.Deserialize<InputRecord>(line, JsonOptions) ?? throw new InvalidDataException();
        if (record.Schema != "kapilab-agent-turn-input-v1" || string.IsNullOrWhiteSpace(record.Id) || record.Id.Length > 256 ||
            record.Input is null || record.Input.Attachments is null || record.Input.Tools is null || record.Input.History is null ||
            record.Input.UserMessage is null || record.Input.History.Count > 126 || record.Input.Tools.Count > 100 ||
            record.DisabledReasoningSuffix is null || record.DisabledReasoningSuffix.Length > 128)
            throw new InvalidDataException();
        // Inspect data before building ChatML; assistant tool calls/thinking are allowed, role markers never are.
        if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(record.Input.SystemPromptExtra ?? ""))
            throw new UnauthorizedAccessException();
        foreach (var message in record.Input.History)
        {
            if (message is null || message.Role is not ("user" or "assistant" or "tool") || message.Content is null || message.Content.Length > 262_144)
                throw new InvalidDataException();
            if (message.Role != "assistant" && CompletionOutputProcessor.ContainsReservedOrSensitiveText(message.Content))
                throw new UnauthorizedAccessException();
            if (message.Content.Contains("<|", StringComparison.Ordinal) || CompletionPrivacy.ContainsSensitiveText(message.Content))
                throw new UnauthorizedAccessException();
        }
        var user = LocalAgentPromptBuilder.BuildUserContent(record.Input.UserMessage, record.Input.AuthorizedContext, record.Input.Attachments)
            ?? throw new InvalidDataException();
        // Escaped JSON must not hide markers or credentials in the original fields.
        var data = record.Input.UserMessage + record.Input.AuthorizedContext + string.Concat(record.Input.Attachments.Select(a => a.DisplayName + a.Content));
        if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(data) ||
            record.Input.Tools.Any(t => t is null || t.Name is null || t.Description is null || t.InputSchemaJson is null ||
                CompletionOutputProcessor.ContainsReservedOrSensitiveText(t.Name + t.Description + t.InputSchemaJson)))
            throw new UnauthorizedAccessException();
        foreach (var tool in record.Input.Tools)
        {
            using var schema = JsonDocument.Parse(tool.InputSchemaJson, new JsonDocumentOptions { MaxDepth = 32 });
            JsonContractValidation.RequireUniqueProperties(schema.RootElement);
            RefuseUnsafeSchemaText(schema.RootElement);
        }
        _ = user;
        return record;
    }

    private static void RefuseUnsafeSchemaText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && CompletionOutputProcessor.ContainsReservedOrSensitiveText(value.GetString()!))
            throw new UnauthorizedAccessException();
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(property.Name)) throw new UnauthorizedAccessException();
                RefuseUnsafeSchemaText(property.Value);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RefuseUnsafeSchemaText(item);
    }

    internal static RenderedRecord Render(InputRecord input, bool emitText)
    {
        if (emitText && (!input.Synthetic || !SyntheticFixtureHashes.Value.Contains(Hash(JsonSerializer.Serialize(input, JsonOptions)))))
            throw new UnauthorizedAccessException();
        var text = Qwen3ChatMlFallbackRenderer.Render(LocalAgentPromptBuilder.Build(input.Input, input.AddGenerationPrompt), input.DisabledReasoningSuffix);
        return new("kapilab-render-v1", input.Id, LocalAgentPromptBuilder.ContractId, Hash(text), Hash(""),
            emitText ? text : null, emitText ? "" : null, emitText ? input : null);
    }

    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static async Task<int> RunAsync(string input, string? output, int? limit, string? ids, bool emitText, string? workspaceOption)
    {
        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null || limit is < 1 or > ContractRenderCommand.MaximumRecords) return (int)ExitCode.Usage;
        if (emitText && (string.IsNullOrWhiteSpace(output) || output == "-")) return (int)ExitCode.Usage;
        try
        {
            var path = Path.GetFullPath(input, workspace);
            LabWorkspace.RefuseBlindInput(path, workspace);
            var requested = ids?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var lines = new List<string>();
            await using var stream = File.OpenRead(path);
            if (stream.Length > 128 * 1024 * 1024) throw new InvalidDataException();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
            var scanned = 0;
            var bytes = 0;
            await foreach (var line in ContractRenderCommand.ReadBoundedLinesAsync(reader))
            {
                if (++scanned > 100_000) throw new InvalidDataException();
                if (line.Length == 0) continue;
                if (lines.Count >= (limit ?? ContractRenderCommand.MaximumRecords)) break;
                var record = ParseLine(line);
                if (!seen.Add(record.Id)) throw new InvalidDataException();
                if (requested is not null && !requested.Contains(record.Id)) continue;
                var serialized = JsonSerializer.Serialize(Render(record, emitText), JsonOptions);
                bytes = checked(bytes + Encoding.UTF8.GetByteCount(serialized));
                if (bytes > 128 * 1024 * 1024) throw new InvalidDataException();
                lines.Add(serialized);
            }
            if (lines.Count == 0 || requested is not null && requested.Except(seen).Any()) throw new InvalidDataException();
            if (string.IsNullOrWhiteSpace(output) || output == "-")
                foreach (var line in lines) await Console.Out.WriteLineAsync(line);
            else
            {
                var destination = LabWorkspace.ResolveOutput(workspace, output);
                LabWorkspace.RefuseInputOverwrite(workspace, destination, [path]);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { await File.WriteAllLinesAsync(temporary, lines, new UTF8Encoding(false)); File.Move(temporary, destination, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            Console.Error.WriteLine($"info contract.render records={lines.Count} textIncluded={emitText}");
            return 0;
        }
        catch (UnauthorizedAccessException) { Console.Error.WriteLine("error privacidade/caminho recusado; conteúdo omitido."); return (int)ExitCode.PrivacyViolation; }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException or InvalidDataException or OverflowException or InvalidOperationException or KeyNotFoundException)
        { Console.Error.WriteLine("error contrato de agente inválido; conteúdo omitido."); return (int)ExitCode.InvalidInput; }
    }
}
