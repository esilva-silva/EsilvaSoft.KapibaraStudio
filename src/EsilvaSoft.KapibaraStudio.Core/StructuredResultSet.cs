using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>One result set of an execution: Console expression, query page, aggregation page or script output.</summary>
public sealed class StructuredResultSet
{
    private StructuredResultSet(int number, ResultOrigin origin, string json, bool isTruncated, ResultCompleteness completeness, string? method)
    {
        Number = number; Origin = origin; Json = json; IsTruncated = isTruncated; Completeness = completeness; Method = method;
    }

    public int Number { get; }
    public ResultOrigin Origin { get; }
    /// <summary>JSON of the whole value as returned; used for results that are not documents (counts, write acknowledgements).</summary>
    public string Json { get; }
    public bool IsTruncated { get; }
    public ResultCompleteness Completeness { get; }
    public string? Method { get; }
    /// <summary>Null when the value is not a list of documents.</summary>
    public IReadOnlyList<StructuredResultDocument>? Documents { get; private set; }

    public string Label => "[" + Number.ToString(CultureInfo.InvariantCulture) + "] " + (Origin.Profile is null && Origin.Database is null ? "valor" : Origin.Destination);

    public static StructuredResultSet FromConsole(ConsoleResultSet set) => FromConsole(set, null);

    public static StructuredResultSet FromConsoleLocalized(ConsoleResultSet set, Func<string, string> localize) => FromConsole(set, localize);

    private static StructuredResultSet FromConsole(ConsoleResultSet set, Func<string, string>? localize)
    {
        ArgumentNullException.ThrowIfNull(set);
        var completeness = set.Method switch
        {
            "find" or "findOne" => set.IsProjected ? ResultCompleteness.PartialProjection : ResultCompleteness.Complete,
            "aggregate" => ResultCompleteness.Derived,
            _ => ResultCompleteness.Unknown
        };
        var origin = new ResultOrigin((localize?.Invoke("consoleExpression") ?? "Console · expressão") + " [" + set.Number.ToString(CultureInfo.InvariantCulture) + "]" + (set.Method is null ? "" : " · " + set.Method),
            set.ProfileId, set.SourceProfile, set.Database, string.IsNullOrEmpty(set.Collection) ? null : set.Collection);
        var result = new StructuredResultSet(set.Number, origin, set.Json, set.IsTruncated, completeness, set.Method);
        if (set.Documents is { } documents) result.Documents = documents.Select((json, index) => new StructuredResultDocument(result, index, json)).ToArray();
        return result;
    }

    public static StructuredResultSet FromDocuments(int number, ResultOrigin origin, IReadOnlyList<string> documents, bool isTruncated, ResultCompleteness completeness, string? method = null)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(documents);
        var result = new StructuredResultSet(number, origin, "", isTruncated, completeness, method);
        result.Documents = documents.Select((json, index) => new StructuredResultDocument(result, index, json)).ToArray();
        return result;
    }
}
