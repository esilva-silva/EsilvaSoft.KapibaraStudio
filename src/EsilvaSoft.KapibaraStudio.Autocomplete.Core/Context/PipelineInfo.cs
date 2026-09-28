using System.Collections.ObjectModel;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>
/// Immutable, conservative field shape after zero or more aggregation stages. It performs no I/O and never parses
/// source text or regular expressions. Unknown means that prior fields must not be offered as certain.
/// </summary>
public sealed class PipelineInfo
{
    private const int MaximumFields = 512;
    private readonly IReadOnlyDictionary<string, PipelineField> _fields;

    private PipelineInfo(PipelineFieldState state, IDictionary<string, PipelineField>? fields, int stageIndex)
    {
        State = state;
        StageIndex = stageIndex;
        _fields = new ReadOnlyDictionary<string, PipelineField>(new Dictionary<string, PipelineField>(fields ?? new Dictionary<string, PipelineField>(StringComparer.Ordinal), StringComparer.Ordinal));
    }

    public PipelineFieldState State { get; }
    public bool IsKnown => State == PipelineFieldState.Known;
    public int StageIndex { get; }
    public IReadOnlyDictionary<string, PipelineField> Fields => _fields;

    public static PipelineInfo From(CollectionSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var fields = new Dictionary<string, PipelineField>(StringComparer.Ordinal);
        foreach (var field in schema.Descendants())
        {
            if (fields.Count >= MaximumFields) return Unknown;
            fields[field.Path] = PipelineField.FromSource(field);
        }
        return new(PipelineFieldState.Known, fields, 0);
    }

    public static PipelineInfo Unknown { get; } = new(PipelineFieldState.Unknown, null, 0);

    /// <summary>
    /// Projeta os campos desta posição do pipeline em um <see cref="CollectionSchema"/> aninhado, pronto para a
    /// resolução de campos por caminho pai e prefixo. Um estado desconhecido devolve um schema vazio.
    /// </summary>
    public CollectionSchema ToSchema()
    {
        if (!IsKnown) return CollectionSchema.Empty;
        var builder = new SchemaBuilder();
        foreach (var field in _fields.Values.OrderBy(field => field.Path, StringComparer.Ordinal))
            builder.AddPipelineField(field.Path, field.Types, field.Traits,
                field.Source is { } source ? source.Evidence | EvidenceSources.Pipeline : EvidenceSources.Pipeline);
        return builder.Build();
    }

    public PipelineInfo Apply(PipelineStage stage, CollectionSchema? lookupSchema = null)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (!IsKnown) return new(PipelineFieldState.Unknown, null, StageIndex + 1);

        var fields = new Dictionary<string, PipelineField>(_fields, StringComparer.Ordinal);
        var result = stage.Name switch
        {
            "$match" or "$sort" or "$limit" or "$skip" or "$sample" => fields,
            "$unwind" => fields,
            "$project" => Project(fields, stage.Properties),
            "$addFields" or "$set" => AddFields(fields, stage.Properties),
            "$unset" => Unset(fields, stage.Properties),
            "$group" => Group(stage.Properties),
            "$lookup" => Lookup(fields, stage.Properties, lookupSchema),
            "$facet" => Facet(fields, stage.Properties),
            "$replaceRoot" or "$replaceWith" => ReplaceRoot(fields, stage.Properties),
            "$count" => Count(stage.Properties),
            _ => null
        };
        return result is null || result.Count > MaximumFields
            ? new(PipelineFieldState.Unknown, null, StageIndex + 1)
            : new(PipelineFieldState.Known, result, StageIndex + 1);
    }

    public PipelineInfo Apply(IEnumerable<PipelineStage> stages, Func<PipelineStage, CollectionSchema?>? lookupSchemas = null)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var current = this;
        foreach (var stage in stages)
        {
            ArgumentNullException.ThrowIfNull(stage);
            current = current.Apply(stage, lookupSchemas?.Invoke(stage));
        }
        return current;
    }

    private static Dictionary<string, PipelineField>? Project(Dictionary<string, PipelineField> input, IReadOnlyList<PipelineStageProperty> properties)
    {
        if (properties.Any(property => property.Value.Kind == PipelineValueKind.Unknown)) return null;
        var includes = properties.Where(property => property.Name != "_id" && property.Value.Kind is PipelineValueKind.Include or PipelineValueKind.Expression or PipelineValueKind.FieldReference).ToArray();
        if (includes.Length > 0)
        {
            if (properties.Any(property => property.Name != "_id" && property.Value.Kind == PipelineValueKind.Exclude)) return null;
            var output = new Dictionary<string, PipelineField>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                if (property.Value.Kind == PipelineValueKind.Exclude) continue;
                if (property.Value.Kind == PipelineValueKind.Include) CopySubtree(input, output, property.Name, property.Name);
                else if (property.Value.Kind == PipelineValueKind.FieldReference) CopySubtree(input, output, property.Value.Text!, property.Name);
                else if (property.Value.Kind == PipelineValueKind.Expression) AddComputed(output, property.Name);
                else return null;
            }
            return output;
        }

        if (properties.All(property => property.Value.Kind == PipelineValueKind.Exclude))
        {
            foreach (var property in properties) RemoveSubtree(input, property.Name);
            return input;
        }
        return null;
    }

    private static Dictionary<string, PipelineField>? AddFields(Dictionary<string, PipelineField> input, IReadOnlyList<PipelineStageProperty> properties)
    {
        foreach (var property in properties)
        {
            if (property.Value.Kind is PipelineValueKind.Unknown or PipelineValueKind.Names or PipelineValueKind.Include or PipelineValueKind.Exclude) return null;
            RemoveSubtree(input, property.Name);
            AddComputed(input, property.Name);
        }
        return input;
    }

    private static Dictionary<string, PipelineField>? Unset(Dictionary<string, PipelineField> input, IReadOnlyList<PipelineStageProperty> properties)
    {
        if (properties.Count != 1 || properties[0].Value.Kind != PipelineValueKind.Names || properties[0].Value.Names is not { Count: > 0 } names) return null;
        foreach (var name in names) RemoveSubtree(input, name);
        return input;
    }

    private static Dictionary<string, PipelineField>? Group(IReadOnlyList<PipelineStageProperty> properties)
    {
        if (properties.Count == 0 || properties.Any(property => property.Name != "_id" && property.Value.Kind is not (PipelineValueKind.Accumulator or PipelineValueKind.NumericAccumulator))) return null;
        var output = new Dictionary<string, PipelineField>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (property.Name == "_id") AddComputed(output, "_id");
            else AddComputed(output, property.Name, property.Value.Kind == PipelineValueKind.NumericAccumulator ? ["number"] : null);
        }
        return output;
    }

    private static Dictionary<string, PipelineField>? Lookup(Dictionary<string, PipelineField> input, IReadOnlyList<PipelineStageProperty> properties, CollectionSchema? foreignSchema)
    {
        var alias = properties.SingleOrDefault(property => property.Name == "as")?.Value;
        if (alias is null) return input;
        if (alias.Kind != PipelineValueKind.Literal || string.IsNullOrEmpty(alias.Text)) return null;
        RemoveSubtree(input, alias.Text);
        input[alias.Text] = PipelineField.Computed(alias.Text, ["array"], FieldTraits.Array | FieldTraits.ArrayOfDocuments);
        var nested = properties.SingleOrDefault(property => property.Name == "pipeline")?.Value;
        if (nested?.Kind == PipelineValueKind.Pipeline && foreignSchema is not null)
            foreignSchema = PipelineInfo.From(foreignSchema).Apply(nested.Stages!).ToSchema();
        if (foreignSchema is null) return input;
        foreach (var field in foreignSchema.Descendants())
        {
            var path = alias.Text + "." + field.Path;
            input[path] = new PipelineField(path, field, new ReadOnlyCollection<string>(field.Types.Keys.Order(StringComparer.Ordinal).ToArray()), field.Flags);
        }
        return input;
    }

    private static Dictionary<string, PipelineField>? Facet(Dictionary<string, PipelineField> input,
        IReadOnlyList<PipelineStageProperty> properties)
    {
        var branches = properties.Where(property => property.Value.Kind == PipelineValueKind.Pipeline && property.Value.Stages is not null)
            .ToDictionary(property => property.Name, property => (IReadOnlyList<PipelineStage>)property.Value.Stages!, StringComparer.Ordinal);
        if (branches.Count == 0)
            branches = properties.FirstOrDefault(property => property.Value.Kind == PipelineValueKind.FacetBranches)?.Value.Branches
                is { } declared ? new Dictionary<string, IReadOnlyList<PipelineStage>>(declared, StringComparer.Ordinal) : [];
        if (branches is null) return null;
        var output = new Dictionary<string, PipelineField>(StringComparer.Ordinal);
        foreach (var (name, stages) in branches)
        {
            var branch = PipelineInfo.From(ToSchema(input)).Apply(stages);
            if (!branch.IsKnown) return null;
            AddComputed(output, name, ["array"], FieldTraits.Array | FieldTraits.ArrayOfDocuments);
            foreach (var field in branch.Fields.Values)
            {
                var path = name + "." + field.Path;
                output[path] = new PipelineField(path, field.Source, field.Types, field.Traits);
            }
        }
        return output;
    }

    private static CollectionSchema ToSchema(IReadOnlyDictionary<string, PipelineField> fields)
    {
        var builder = new SchemaBuilder();
        foreach (var field in fields.Values)
            builder.AddPipelineField(field.Path, field.Types, field.Traits, EvidenceSources.Pipeline);
        return builder.Build();
    }

    private static Dictionary<string, PipelineField>? Count(IReadOnlyList<PipelineStageProperty> properties)
    {
        if (properties.Count != 1 || properties[0].Value.Kind != PipelineValueKind.Literal || string.IsNullOrEmpty(properties[0].Value.Text)) return null;
        var name = properties[0].Value.Text!;
        return new(StringComparer.Ordinal) { [name] = PipelineField.Computed(name, ["number"]) };
    }

    private static Dictionary<string, PipelineField>? ReplaceRoot(Dictionary<string, PipelineField> input,
        IReadOnlyList<PipelineStageProperty> properties)
    {
        var reference = properties.SingleOrDefault(property => property.Name is "newRoot" or "replacement")?.Value;
        if (reference?.Kind != PipelineValueKind.FieldReference || string.IsNullOrEmpty(reference.Text)) return null;
        var output = new Dictionary<string, PipelineField>(StringComparer.Ordinal);
        CopySubtree(input, output, reference.Text!, "");
        return output.Count == 0 ? null : output;
    }

    private static void CopySubtree(IReadOnlyDictionary<string, PipelineField> source, Dictionary<string, PipelineField> destination, string sourcePath, string destinationPath)
    {
        foreach (var (path, field) in source)
        {
            if (path != sourcePath && !path.StartsWith(sourcePath + ".", StringComparison.Ordinal)) continue;
            var target = destinationPath + path[sourcePath.Length..];
            if (target.StartsWith('.')) target = target[1..];
            if (target.Length == 0) continue;
            destination[target] = new PipelineField(target, field.Source, field.Types, field.Traits);
        }
    }

    private static void AddComputed(Dictionary<string, PipelineField> fields, string path, IEnumerable<string>? types = null, FieldTraits traits = FieldTraits.None)
    {
        var prefix = path.IndexOf('.', StringComparison.Ordinal);
        while (prefix >= 0)
        {
            var parent = path[..prefix];
            fields.TryAdd(parent, PipelineField.Computed(parent));
            prefix = path.IndexOf('.', prefix + 1);
        }
        fields[path] = PipelineField.Computed(path, types, traits);
    }

    private static void RemoveSubtree(Dictionary<string, PipelineField> fields, string path)
    {
        foreach (var name in fields.Keys.Where(name => name == path || name.StartsWith(path + ".", StringComparison.Ordinal)).ToArray()) fields.Remove(name);
    }
}
