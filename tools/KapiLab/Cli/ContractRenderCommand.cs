using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class ContractRenderCommand
{
    private const int MaximumInputLineBytes = 4 * 1024 * 1024;
    internal const int MaximumRecords = 20_000;
    private const int MaximumOutputBytes = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<int> RunAsync(string input, string contract, string? output, int? limit,
        string? ids, bool noEditorContext, string? workspaceOption)
    {
        if (!string.Equals(contract, "editor-context-v1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("error contrato ainda não suportado por este comando.");
            return (int)ExitCode.Drift;
        }
        if (limit is < 1 or > MaximumRecords)
        {
            Console.Error.WriteLine($"error --limit deve estar entre 1 e {MaximumRecords}.");
            return (int)ExitCode.Usage;
        }

        string? workspace = null;
        string inputPath;
        var requestedIds = string.IsNullOrWhiteSpace(ids)
            ? null
            : ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var environment = EnvironmentReport.Create();
        var lineNumber = 0;
        var selected = 0;
        var totalBytes = 0;
        try
        {
            workspace = LabWorkspace.Resolve(workspaceOption);
            if (workspace is null)
            {
                Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE para selecionar o laboratório.");
                return (int)ExitCode.Usage;
            }
            inputPath = Path.GetFullPath(input, workspace);
            LabWorkspace.RefuseBlindInput(inputPath, workspace);
            await using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Arquivo de entrada excede o limite de 2 GiB.");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            await foreach (var line in ReadBoundedLinesAsync(reader).ConfigureAwait(false))
            {
                lineNumber++;
                if (lineNumber > 100_000) throw new InvalidDataException("Arquivo excede o limite de 100.000 linhas.");
                if (line.Length == 0) continue;
                if (selected >= (limit ?? MaximumRecords)) break;
                var rendered = RenderLine(line, lineNumber, noEditorContext, environment);
                if (!seen.Add(rendered.Id)) throw new InvalidDataException($"Linha {lineNumber}: id duplicado.");
                if (requestedIds is not null && !requestedIds.Contains(rendered.Id)) continue;
                selected++;
                var serialized = JsonSerializer.Serialize(rendered, JsonOptions);
                totalBytes = checked(totalBytes + Encoding.UTF8.GetByteCount(serialized) + 1);
                if (totalBytes > MaximumOutputBytes) throw new InvalidDataException("Saída excede o limite de 128 MiB.");
                results.Add(serialized);
            }

            if (requestedIds is not null)
            {
                var missing = requestedIds.Except(seen, StringComparer.Ordinal).ToArray();
                if (missing.Length > 0) throw new InvalidDataException("Um ou mais IDs solicitados não foram encontrados.");
            }

            if (string.IsNullOrWhiteSpace(output) || output == "-")
            {
                foreach (var result in results) await Console.Out.WriteLineAsync(result).ConfigureAwait(false);
            }
            else
            {
                if (workspace is null) throw new UnauthorizedAccessException("Use --workspace ou KAPILAB_WORKSPACE para gravar artefatos.");
                var destination = LabWorkspace.ResolveOutput(workspace, output);
                LabWorkspace.RefuseInputOverwrite(workspace, destination, [inputPath]);
                await AtomicWriteAsync(destination, results).ConfigureAwait(false);
            }
            Console.Error.WriteLine($"info contract.render records={results.Count} bytes={totalBytes}");
            return (int)ExitCode.Success;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"error {exception.Message}");
            return (int)ExitCode.PrivacyViolation;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or ArgumentException or OverflowException)
        {
            Console.Error.WriteLine($"error contract.render: {SafeError(exception)}");
            return (int)ExitCode.InvalidInput;
        }
    }

    internal static async IAsyncEnumerable<string> ReadBoundedLinesAsync(StreamReader reader)
    {
        var buffer = new char[8192];
        var line = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0)
            {
                if (line.Length > 0) yield return TrimCarriageReturn(line);
                yield break;
            }
            for (var index = 0; index < count; index++)
            {
                if (buffer[index] == '\n')
                {
                    yield return TrimCarriageReturn(line);
                    line.Clear();
                }
                else
                {
                    line.Append(buffer[index]);
                    if (line.Length > MaximumInputLineBytes)
                        throw new InvalidDataException("Linha excede o limite de 4 MiB.");
                }
            }
        }
    }

    private static string TrimCarriageReturn(StringBuilder line)
    {
        if (line.Length > 0 && line[^1] == '\r') line.Length--;
        return line.ToString();
    }

    internal static RenderedRecord RenderLine(string line, int lineNumber, bool noEditorContext, EnvironmentReport? environment = null)
    {
        var settings = new AutocompleteSettings
        {
            UseEditorContext = !noEditorContext,
            UseResultPanelContext = !noEditorContext,
            UseInputPanelContext = !noEditorContext,
        };
        var parsed = ParseInputLine(line, lineNumber, settings);
        var modelPrefix = AutocompleteContextBuilder.ModelPrefix(parsed.Request);
        var prefixBytes = Encoding.UTF8.GetBytes(modelPrefix);
        var suffixBytes = Encoding.UTF8.GetBytes(parsed.Request.Suffix);
        var source = environment ?? EnvironmentReport.Create();
        return new("kapilab-render-v1", parsed.Id, "editor-context-v1", modelPrefix, parsed.Request.Suffix,
            Convert.ToHexStringLower(SHA256.HashData(prefixBytes)), Convert.ToHexStringLower(SHA256.HashData(suffixBytes)),
            new(parsed.PrefixCharacters, parsed.SuffixCharacters, parsed.Request.Context.Length, parsed.Request.Dictionary.Count),
            new(source.IdeCommit, source.GenAiVersion));
    }

    internal static ParsedInputRecord ParseInputLine(string line, int lineNumber, AutocompleteSettings settings)
    {
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !TryString(root, "id", out var id) || id.Length is 0 or > 256 ||
            !TryString(root, "type", out var type) || type is not ("fim" or "completion") ||
            !TryString(root, "prefix", out var prefix) || !TryString(root, "suffix", out var suffix))
            throw new InvalidDataException($"Linha {lineNumber}: registro inválido; id, type, prefix e suffix são obrigatórios.");
        var language = TryString(root, "language", out var suppliedLanguage) ? suppliedLanguage : "Mongo Console JavaScript";
        var editor = root.TryGetProperty("editor", out var editorValue) && editorValue.ValueKind == JsonValueKind.Object
            ? editorValue : default;
        var knownNames = ReadStringArray(editor, "known_names", lineNumber);
        var resultFields = ReadStringArray(editor, "result_fields", lineNumber);
        var recentCommands = ReadStringArray(editor, "recent_commands", lineNumber);
        var inputPanel = ReadString(editor, "input_panel", "", lineNumber);
        var combinedText = string.Concat(prefix, suffix, inputPanel, string.Join('\n', knownNames),
            string.Join('\n', resultFields), string.Join('\n', recentCommands));
        if (CompletionPrivacy.ContainsSensitiveText(combinedText) || combinedText.Contains("<|", StringComparison.Ordinal) || combinedText.Contains("<｜", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Entrada contém texto sensível ou marcador reservado; conteúdo não foi processado.");

        var snapshot = new AutocompleteContextSnapshot(string.Concat(prefix, suffix), prefix.Length, language,
            inputPanel, resultFields, knownNames, recentCommands,
            TryString(editor, "target", out var target) ? target : null);
        var request = AutocompleteContextBuilder.Build(snapshot, settings);
        return new(id, type, request, prefix.Length, suffix.Length);
    }

    private static string ReadString(JsonElement parent, string propertyName, string fallback, int lineNumber)
    {
        if (!parent.ValueKind.Equals(JsonValueKind.Object) || !parent.TryGetProperty(propertyName, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"Linha {lineNumber}: campo {propertyName} precisa ser texto.");
        return value.GetString() ?? fallback;
    }

    private static List<string> ReadStringArray(JsonElement parent, string propertyName, int lineNumber)
    {
        if (!parent.ValueKind.Equals(JsonValueKind.Object) || !parent.TryGetProperty(propertyName, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"Linha {lineNumber}: campo {propertyName} precisa ser uma lista.");
        var values = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw new InvalidDataException($"Linha {lineNumber}: campo {propertyName} contém item não textual.");
            values.Add(item.GetString() ?? "");
            if (values.Count > 256) throw new InvalidDataException($"Linha {lineNumber}: campo {propertyName} excede 256 itens.");
        }
        return values;
    }

    private static bool TryString(JsonElement parent, string propertyName, out string value)
    {
        value = "";
        if (!parent.ValueKind.Equals(JsonValueKind.Object) || !parent.TryGetProperty(propertyName, out var item) || item.ValueKind != JsonValueKind.String)
            return false;
        value = item.GetString() ?? "";
        return true;
    }

    private static async Task AtomicWriteAsync(string path, IReadOnlyList<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                foreach (var line in lines) await writer.WriteLineAsync(line).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string SafeError(Exception exception) => exception switch
    {
        JsonException => "JSON inválido.",
        InvalidDataException => exception.Message,
        UnauthorizedAccessException => "acesso recusado.",
        IOException => "falha de leitura ou escrita.",
        _ => "entrada rejeitada.",
    };

    internal sealed record RenderedRecord(string Schema, string Id, string Contract, string ModelPrefix, string Suffix,
        string ModelPrefixSha256, string SuffixSha256, RequestSummary Request, IdeSummary Ide);
    internal sealed record ParsedInputRecord(string Id, string Type, AutocompleteRequest Request,
        int PrefixCharacters, int SuffixCharacters);
    internal sealed record RequestSummary(int PrefixChars, int SuffixChars, int ContextChars, int DictionaryCount);
    internal sealed record IdeSummary(string? Commit, string GenAi);
}
