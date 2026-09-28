using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public static class MqlAutocompleteService
{
    public static IReadOnlySet<string> InferFieldPaths(IEnumerable<string> documents, int maximumDepth = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        // The catalog schema builder owns field inference; paths keep their discovery order.
        return new SchemaBuilder(maximumDepth, int.MaxValue).AddDocuments(documents).Build().Paths().ToHashSet(StringComparer.Ordinal);
    }

    public static string InferJsonSchema(IEnumerable<string> documents, int maximumDepth = 12) => JsonSchemaInference.Infer(documents, maximumDepth);
}
