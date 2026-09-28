using System.Collections.ObjectModel;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>A field safe to offer at the current pipeline position. Source is present only for unmodified catalog fields.</summary>
public sealed record PipelineField(string Path, FieldNode? Source, IReadOnlyCollection<string> Types, FieldTraits Traits)
{
    internal static PipelineField FromSource(FieldNode field) => new(field.Path, field, new ReadOnlyCollection<string>(field.Types.Keys.Order(StringComparer.Ordinal).ToArray()), field.Flags);
    internal static PipelineField Computed(string path, IEnumerable<string>? types = null, FieldTraits traits = FieldTraits.None) =>
        new(path, null, new ReadOnlyCollection<string>((types ?? ["unknown"]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()), traits);
}
