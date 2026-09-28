using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public static class QueryResultExportSerializer
{
    /// <summary>Writes the bounded loaded page one document at a time. Caller owns the destination.</summary>
    public static async Task WriteAsync(Stream destination, IReadOnlyList<string> documents, bool csv,
        Action<int, int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(documents);
        if (!csv)
        {
            await using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });
            writer.WriteStartArray();
            for (var i = 0; i < documents.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var parsed = JsonDocument.Parse(documents[i], new JsonDocumentOptions { MaxDepth = ExtendedJsonFormatter.MaxDepth });
                parsed.RootElement.WriteTo(writer);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                progress?.Invoke(i + 1, documents.Count);
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // Two bounded passes: discover the union of columns, then serialize each row independently.
        var headers = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var parsed = JsonDocument.Parse(document, new JsonDocumentOptions { MaxDepth = ExtendedJsonFormatter.MaxDepth });
            foreach (var property in parsed.RootElement.EnumerateObject())
                if (known.Add(property.Name)) headers.Add(property.Name);
        }
        using var output = new StreamWriter(destination, new System.Text.UTF8Encoding(false), 16384, leaveOpen: true);
        await output.WriteLineAsync(string.Join(',', headers.Select(EscapeCsv)).AsMemory(), cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < documents.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var parsed = JsonDocument.Parse(documents[i], new JsonDocumentOptions { MaxDepth = ExtendedJsonFormatter.MaxDepth });
            for (var column = 0; column < headers.Count; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (column > 0) await output.WriteAsync(",".AsMemory(), cancellationToken).ConfigureAwait(false);
                if (!parsed.RootElement.TryGetProperty(headers[column], out var value)) continue;
                var cell = value.ValueKind == JsonValueKind.String ? value.GetString()! : JsonSerializer.Serialize(value);
                await output.WriteAsync(EscapeCsv(cell, value.ValueKind == JsonValueKind.String).AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            await output.WriteAsync("\r\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            progress?.Invoke(i + 1, documents.Count);
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // Spreadsheet-safe policy applies to strings and headers. Apostrophe is deliberately part of the exported data.
    private static string EscapeCsv(string value) => EscapeCsv(value, true);
    private static string EscapeCsv(string value, bool protectFormula)
    {
        var trimmed = value.AsSpan().TrimStart();
        if (protectFormula && (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string Serialize(IReadOnlyList<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        foreach (var document in documents)
        {
            using var parsed = JsonDocument.Parse(document, new JsonDocumentOptions { MaxDepth = ExtendedJsonFormatter.MaxDepth });
            parsed.RootElement.WriteTo(writer);
        }
        writer.WriteEndArray();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
