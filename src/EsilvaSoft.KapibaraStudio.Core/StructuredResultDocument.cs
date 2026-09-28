using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>A returned document with its position and origin. <see cref="Json"/> is the original Extended JSON, never re-serialized.</summary>
public sealed class StructuredResultDocument
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = ExtendedJsonFormatter.MaxDepth };

    internal StructuredResultDocument(StructuredResultSet set, int position, string json)
    {
        Set = set; Position = position; Json = json;
        try
        {
            using var document = JsonDocument.Parse(json, Options);
            Root = document.RootElement.Clone();
            if (Root.Value.ValueKind == JsonValueKind.Object && Root.Value.TryGetProperty("_id", out var id)) IdJson = id.GetRawText();
        }
        catch (JsonException exception)
        {
            InvalidJsonMessage = exception.Message;
        }
    }

    public StructuredResultSet Set { get; }
    /// <summary>Zero-based position inside <see cref="Set"/>.</summary>
    public int Position { get; }
    public string Json { get; }
    public JsonElement? Root { get; }
    /// <summary>Raw Extended JSON of <c>_id</c>, when present.</summary>
    public string? IdJson { get; }
    public string? InvalidJsonMessage { get; }
    public bool IsValid => InvalidJsonMessage is null;
    public string? IdentityFilter => IdJson is null ? null : "{\"_id\":" + IdJson + "}";
    public bool IsTruncated => Set.IsTruncated;
    public bool IsPartialProjection => Set.Completeness == ResultCompleteness.PartialProjection;

    /// <summary>Builds a document without known origin, for callers that only hold its JSON.</summary>
    public static StructuredResultDocument Detached(string json, int position = 0)
    {
        var set = StructuredResultSet.FromDocuments(1, ResultOrigin.Unknown, [json], false, ResultCompleteness.Unknown);
        return position == 0 ? set.Documents![0] : new StructuredResultDocument(set, position, json);
    }
}
