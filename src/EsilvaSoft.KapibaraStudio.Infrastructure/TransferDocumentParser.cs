using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Line and field diagnostic without the source value, which may contain sensitive data.</summary>
public sealed class TransferRowException(int line, string field, string code, string message) : FormatException(message)
{
    public int Line { get; } = line;
    public string Field { get; } = field;
    public string Code { get; } = code;
}

public sealed record TransferParsedRow(int Line, BsonDocument Document);

/// <summary>Standalone streaming JSON/NDJSON/CSV parser. It does not open files or connect to MongoDB.</summary>
public static class TransferDocumentParser
{
    public const int MaximumRows = 1_000_000;
    public const int MaximumRecordCharacters = 16 * 1024 * 1024;
    public const int MaximumBsonBytes = 16 * 1024 * 1024;
    public const int MaximumPreviewRows = 20;

    public static async IAsyncEnumerable<TransferParsedRow> ParseAsync(
        TextReader reader,
        TransferImportSchema schema,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        schema.Validate();
        var cursor = new BoundedTextCursor(reader);
        var rowCount = 0;

        if (schema.Format == TransferImportFormat.Ndjson)
        {
            while (await cursor.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line.Value)) continue;
                var document = ParseEjsonDocument(line.Value, line.StartLine);
                EnsureDocumentSize(document, line.StartLine);
                if (++rowCount > MaximumRows)
                    throw Diagnostic(line.StartLine, "*", "row-limit", "A origem excede 1.000.000 de documentos.");
                yield return new TransferParsedRow(line.StartLine, document);
            }
            yield break;
        }

        if (schema.Format == TransferImportFormat.JsonArray)
        {
            while (await cursor.ReadJsonArrayObjectAsync(cancellationToken).ConfigureAwait(false) is { } json)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = ParseEjsonDocument(json.Value, json.StartLine);
                EnsureDocumentSize(document, json.StartLine);
                if (++rowCount > MaximumRows)
                    throw Diagnostic(json.StartLine, "*", "row-limit", "A origem excede 1.000.000 de documentos.");
                yield return new TransferParsedRow(json.StartLine, document);
            }
            yield break;
        }

        var columns = schema.Columns ?? throw new InvalidOperationException("Mapeamento CSV ausente após validação.");
        var header = await cursor.ReadCsvRecordAsync(cancellationToken).ConfigureAwait(false)
            ?? throw Diagnostic(1, "*", "missing-header", "CSV não contém cabeçalho.");
        ValidateHeader(header.Fields, columns, header.StartLine);
        var indexes = columns.Select(column => Array.IndexOf(header.Fields, column.SourceColumn)).ToArray();

        while (await cursor.ReadCsvRecordAsync(cancellationToken).ConfigureAwait(false) is { } row)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Fields.Length != header.Fields.Length)
                throw Diagnostic(row.StartLine, "*", "column-count", $"A linha {row.StartLine} tem {row.Fields.Length} coluna(s); o cabeçalho declara {header.Fields.Length}.");

            var document = new BsonDocument();
            for (var index = 0; index < columns.Count; index++)
            {
                var mapping = columns[index];
                document.Add(mapping.TargetField, ConvertCell(row.Fields[indexes[index]], mapping, row.StartLine));
            }
            if (document["_id"].IsBsonNull)
                throw Diagnostic(row.StartLine, "_id", "required-id", $"Linha {row.StartLine}: _id não pode ser nulo.");
            EnsureDocumentSize(document, row.StartLine);
            if (++rowCount > MaximumRows)
                throw Diagnostic(row.StartLine, "*", "row-limit", "A origem excede 1.000.000 de documentos.");
            yield return new TransferParsedRow(row.StartLine, document);
        }
    }

    public static async Task<TransferDocumentPreview> PreviewAsync(
        TextReader reader,
        TransferImportSchema schema,
        CancellationToken cancellationToken = default)
    {
        var types = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var sampled = 0;
        var hasMore = false;
        await foreach (var row in ParseAsync(reader, schema, cancellationToken).ConfigureAwait(false))
        {
            if (sampled == MaximumPreviewRows)
            {
                hasMore = true;
                break;
            }
            sampled++;
            foreach (var field in row.Document)
            {
                if (!types.TryGetValue(field.Name, out var observed))
                {
                    if (types.Count == 256)
                        throw Diagnostic(row.Line, field.Name, "field-limit", "A prévia excede 256 campos distintos.");
                    types[field.Name] = observed = new HashSet<string>(StringComparer.Ordinal);
                }
                observed.Add(field.Value.BsonType.ToString());
            }
        }

        IReadOnlyList<TransferPreviewField> fields = schema.Format == TransferImportFormat.Csv
            ? schema.Columns!.Select(mapping => new TransferPreviewField(mapping.TargetField, mapping.Type.ToString(), mapping.SourceColumn)).ToArray()
            : types.Select(pair => new TransferPreviewField(pair.Key, string.Join("/", pair.Value.Order(StringComparer.Ordinal)))).ToArray();
        return new TransferDocumentPreview(
            fields,
            sampled,
            hasMore);
    }

    private static void ValidateHeader(string[] fields, IReadOnlyList<TransferColumnMapping> columns, int line)
    {
        if (fields.Length is 0 or > 256 || fields.Any(string.IsNullOrWhiteSpace)
            || fields.Distinct(StringComparer.Ordinal).Count() != fields.Length
            || fields.Length != columns.Count
            || columns.Any(column => !fields.Contains(column.SourceColumn, StringComparer.Ordinal)))
            throw Diagnostic(line, "*", "mapping-mismatch", "O cabeçalho CSV não corresponde exatamente ao mapeamento; revise nomes, duplicados e colunas ausentes.");
    }

    private static BsonDocument ParseEjsonDocument(string json, int line)
    {
        try
        {
            using var parsed = System.Text.Json.JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw Diagnostic(line, "*", "expected-object", $"Linha {line}: esperado objeto Extended JSON.");
            var document = BsonDocument.Parse(json);
            if (!document.Contains("_id") || document["_id"].IsBsonNull)
                throw Diagnostic(line, "_id", "required-id", $"Linha {line}: _id é obrigatório e não pode ser nulo.");
            return document;
        }
        catch (TransferRowException) { throw; }
        catch (Exception exception) when (exception is FormatException or BsonException or System.Text.Json.JsonException)
        {
            throw Diagnostic(line, "*", "invalid-ejson", $"Linha {line}: Extended JSON inválido; revise a sintaxe do documento.");
        }
    }

    private static BsonValue ConvertCell(string text, TransferColumnMapping mapping, int line)
    {
        if (text.Length == 0 && mapping.EmptyAsNull) return BsonNull.Value;
        try
        {
            return mapping.Type switch
            {
                TransferFieldType.Text => new BsonString(text),
                TransferFieldType.Boolean => bool.TryParse(text, out var value) ? new BsonBoolean(value) : throw new FormatException(),
                TransferFieldType.Integer32 => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? new BsonInt32(value) : throw new FormatException(),
                TransferFieldType.Integer64 => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? new BsonInt64(value) : throw new FormatException(),
                TransferFieldType.FloatingPoint => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? new BsonDouble(value) : throw new FormatException(),
                TransferFieldType.Decimal128 => new BsonDecimal128(Decimal128.Parse(text)),
                TransferFieldType.DateTimeUtc => HasExplicitOffset(text)
                    && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                    ? new BsonDateTime(value.UtcDateTime) : throw new FormatException(),
                TransferFieldType.ObjectId => ObjectId.TryParse(text, out var value) ? new BsonObjectId(value) : throw new FormatException(),
                TransferFieldType.UuidStandard => Guid.TryParseExact(text, "D", out var value)
                    ? new BsonBinaryData(value, GuidRepresentation.Standard) : throw new FormatException(),
                TransferFieldType.ExtendedJson => BsonDocument.Parse("{\"value\":" + text + "}")["value"],
                _ => throw new FormatException()
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or BsonException or System.Text.Json.JsonException)
        {
            throw Diagnostic(line, mapping.SourceColumn, "invalid-value",
                $"Linha {line}, coluna {mapping.SourceColumn}: valor incompatível com o tipo {mapping.Type}; revise o mapeamento ou a célula.");
        }
    }

    private static bool HasExplicitOffset(string text)
    {
        if (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Length < 6) return false;
        var offset = text.AsSpan(text.Length - 6);
        return (offset[0] is '+' or '-') && char.IsDigit(offset[1]) && char.IsDigit(offset[2])
            && offset[3] == ':' && char.IsDigit(offset[4]) && char.IsDigit(offset[5]);
    }

    private static void EnsureDocumentSize(BsonDocument document, int line)
    {
        if (document.ToBson().Length > MaximumBsonBytes)
            throw Diagnostic(line, "*", "document-too-large", $"Linha {line}: documento excede 16 MiB BSON.");
    }

    private static TransferRowException Diagnostic(int line, string field, string code, string message) => new(line, field, code, message);

    private sealed class BoundedTextCursor(TextReader reader)
    {
        private readonly char[] _buffer = new char[4096];
        private int _offset;
        private int _length;
        private int _line = 1;
        private bool _jsonArrayStarted;
        private bool _jsonArrayEnded;
        private bool _jsonArrayAfterComma;

        private async ValueTask<int> PeekAsync(CancellationToken cancellationToken)
        {
            if (_offset < _length) return _buffer[_offset];
            _length = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            _offset = 0;
            return _length == 0 ? -1 : _buffer[0];
        }

        private async ValueTask<int> ReadAsync(CancellationToken cancellationToken)
        {
            var character = await PeekAsync(cancellationToken).ConfigureAwait(false);
            if (character < 0) return character;
            _offset++;
            if (character == '\n') _line++;
            return character;
        }

        public async ValueTask<(string Value, int StartLine)?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = _line;
            var value = new StringBuilder();
            while (await ReadAsync(cancellationToken).ConfigureAwait(false) is var character && character >= 0)
            {
                if (character == '\n') return (value.ToString().TrimEnd('\r'), line);
                if (value.Length == MaximumRecordCharacters)
                    throw Diagnostic(line, "*", "record-too-large", $"Linha {line}: registro excede 16 milhões de caracteres.");
                value.Append((char)character);
            }
            return value.Length == 0 ? null : (value.ToString().TrimEnd('\r'), line);
        }

        public async ValueTask<(string Value, int StartLine)?> ReadJsonArrayObjectAsync(CancellationToken cancellationToken)
        {
            if (_jsonArrayEnded) return null;
            if (!_jsonArrayStarted)
            {
                var opening = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
                if (opening.Character != '[')
                    throw Diagnostic(opening.Line, "*", "expected-array", "JSON avulso deve ser um array de documentos.");
                _jsonArrayStarted = true;
            }

            var first = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
            if (first.Character == ']')
            {
                if (_jsonArrayAfterComma)
                    throw Diagnostic(first.Line, "*", "trailing-comma", "O array JSON não pode terminar com vírgula.");
                var trailing = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
                if (trailing.Character >= 0)
                    throw Diagnostic(trailing.Line, "*", "trailing-content", "Há conteúdo após o array JSON.");
                _jsonArrayEnded = true;
                return null;
            }
            if (first.Character < 0)
                throw Diagnostic(first.Line, "*", "incomplete-array", "O array JSON não foi encerrado.");
            else if (first.Character != '{')
                throw Diagnostic(first.Line, "*", "expected-object", "Cada item do array JSON precisa ser um documento.");

            var startLine = first.Line;
            var value = new StringBuilder().Append('{');
            var depth = 1;
            var inString = false;
            var escaped = false;
            while (depth > 0)
            {
                var (character, line) = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
                if (character < 0)
                    throw Diagnostic(startLine, "*", "incomplete-object", "Um documento do array JSON não foi encerrado.");
                if (value.Length == MaximumRecordCharacters)
                    throw Diagnostic(startLine, "*", "record-too-large", "Documento JSON excede 16 milhões de caracteres.");
                value.Append((char)character);

                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == '"') inString = false;
                    continue;
                }
                if (character == '"') inString = true;
                else if (character is '{' or '[') depth++;
                else if (character is '}' or ']') depth--;
                _ = line;
            }

            var separator = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
            if (separator.Character == ']')
            {
                var trailing = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
                if (trailing.Character >= 0)
                    throw Diagnostic(trailing.Line, "*", "trailing-content", "Há conteúdo após o array JSON.");
                _jsonArrayEnded = true;
                _jsonArrayAfterComma = false;
            }
            else if (separator.Character == ',')
            {
                _jsonArrayAfterComma = true;
            }
            else
            {
                throw Diagnostic(separator.Line, "*", "expected-separator", "Esperado ',' ou ']' após documento JSON.");
            }

            return (value.ToString(), startLine);
        }

        private async ValueTask<(int Character, int Line)> ReadNonWhitespaceAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var (character, line) = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
                if (character < 0 || !char.IsWhiteSpace((char)character)) return (character, line);
            }
        }

        private async ValueTask<(int Character, int Line)> ReadCharacterAsync(CancellationToken cancellationToken)
        {
            var line = _line;
            return (await ReadAsync(cancellationToken).ConfigureAwait(false), line);
        }

        public async ValueTask<(string[] Fields, int StartLine)?> ReadCsvRecordAsync(CancellationToken cancellationToken)
        {
            var line = _line;
            var fields = new List<string>();
            var value = new StringBuilder();
            var quoted = false;
            var afterQuote = false;
            var any = false;
            var size = 0;
            while (await ReadAsync(cancellationToken).ConfigureAwait(false) is var character && character >= 0)
            {
                any = true;
                if (++size > MaximumRecordCharacters)
                    throw Diagnostic(line, "*", "record-too-large", $"Linha {line}: registro excede 16 milhões de caracteres.");
                if (quoted)
                {
                    if (character == '"')
                    {
                        if (await PeekAsync(cancellationToken).ConfigureAwait(false) == '"')
                        {
                            _ = await ReadAsync(cancellationToken).ConfigureAwait(false);
                            value.Append('"');
                        }
                        else { quoted = false; afterQuote = true; }
                    }
                    else value.Append((char)character);
                    continue;
                }
                if (character == ',')
                {
                    fields.Add(value.ToString());
                    value.Clear();
                    afterQuote = false;
                    continue;
                }
                if (character is '\n' or '\r')
                {
                    if (character == '\r' && await PeekAsync(cancellationToken).ConfigureAwait(false) == '\n')
                        _ = await ReadAsync(cancellationToken).ConfigureAwait(false);
                    fields.Add(value.ToString());
                    return (fields.ToArray(), line);
                }
                if (character == '"' && value.Length == 0 && !afterQuote)
                {
                    quoted = true;
                    continue;
                }
                if (afterQuote || character == '"')
                    throw Diagnostic(_line, $"{fields.Count + 1}", "invalid-csv", $"Linha {_line}, coluna {fields.Count + 1}: aspas CSV inválidas.");
                value.Append((char)character);
            }
            if (quoted)
                throw Diagnostic(line, $"{fields.Count + 1}", "invalid-csv", $"Linha {line}: campo CSV entre aspas não foi fechado.");
            if (!any) return null;
            fields.Add(value.ToString());
            return (fields.ToArray(), line);
        }
    }
}
