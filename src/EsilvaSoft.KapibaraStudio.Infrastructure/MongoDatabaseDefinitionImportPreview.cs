using System.Text.Json;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Reads only the package manifest and target namespace names for an explicit human preview.</summary>
internal static class MongoDatabaseDefinitionImportPreview
{
    private const int MaximumManifestCharacters = 4 * 1024 * 1024;

    public static async Task<DatabaseDefinitionImportPreview> CreateAsync(
        IMongoDatabaseExportFileAccess files, string sourceDirectory,
        Func<CancellationToken, Task<IReadOnlyList<string>>> readTargetNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(readTargetNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var directory = files.NormalizePath(sourceDirectory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!files.DirectoryExists(directory) || !files.FileExists(manifestPath))
            throw new ArgumentException("O pacote não contém um manifesto acessível.", nameof(sourceDirectory));

        if (files is not IStreamingMongoDatabaseExportFileAccess streaming)
            throw new NotSupportedException("A prévia exige leitura incremental do manifesto.");
        using var stream = streaming.OpenRead(manifestPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = new StringBuilder();
        var buffer = new char[8192];
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (content.Length + count > MaximumManifestCharacters)
                throw new ArgumentException("O manifesto excede o limite da prévia.", nameof(sourceDirectory));
            content.Append(buffer, 0, count);
        }

        PreviewManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PreviewManifest>(content.ToString())
                ?? throw new ArgumentException("O manifesto está vazio.", nameof(sourceDirectory));
            if (manifest.FormatVersion != 3 || manifest.Collections is null || manifest.Views is null
                || manifest.Definitions is null)
                throw new ArgumentException("O pacote não contém definições v3 compatíveis.", nameof(sourceDirectory));
            manifest.Definitions.Validate();
            ValidateConsistency(manifest);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new ArgumentException("As definições do manifesto não são compatíveis com a prévia.", nameof(sourceDirectory), exception);
        }

        var definitions = manifest.Definitions!;
        var existing = (await readTargetNames(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        var items = new List<DatabaseDefinitionImportPreviewItem>();
        var blockedSources = existing.ToHashSet(StringComparer.Ordinal);
        foreach (var collection in definitions.Collections)
        {
            var collision = existing.Contains(collection.Name)
                ? DatabaseDefinitionCollision.Conflicting : DatabaseDefinitionCollision.None;
            using var options = JsonDocument.Parse(collection.OptionsJson);
            var hasOptions = options.RootElement.EnumerateObject().Any();
            items.Add(new(DatabaseDefinitionKind.CollectionOptions, collection.Name, null, [], collision,
                collision == DatabaseDefinitionCollision.Conflicting ? DatabaseDefinitionPlannedStatus.Blocked
                : hasOptions ? DatabaseDefinitionPlannedStatus.Attempt : DatabaseDefinitionPlannedStatus.Omitted));
            if (collection.Validation is not null)
                items.Add(new(DatabaseDefinitionKind.Validator, collection.Name, null, [collection.Name], collision,
                    collision == DatabaseDefinitionCollision.Conflicting ? DatabaseDefinitionPlannedStatus.Blocked
                    : DatabaseDefinitionPlannedStatus.Attempt));
            foreach (var index in collection.Indexes)
            {
                var defaultId = IsDefaultIdIndex(index);
                var conflictingId = string.Equals(index.Name, "_id_", StringComparison.Ordinal) && !defaultId;
                items.Add(new(DatabaseDefinitionKind.Index, collection.Name, index.Name, [collection.Name],
                    collision == DatabaseDefinitionCollision.Conflicting ? collision
                    : defaultId ? DatabaseDefinitionCollision.Identical
                    : conflictingId ? DatabaseDefinitionCollision.Conflicting : DatabaseDefinitionCollision.None,
                    collision == DatabaseDefinitionCollision.Conflicting ? DatabaseDefinitionPlannedStatus.Blocked
                    : defaultId ? DatabaseDefinitionPlannedStatus.Omitted
                    : conflictingId ? DatabaseDefinitionPlannedStatus.Blocked : DatabaseDefinitionPlannedStatus.Attempt));
            }
        }

        foreach (var view in definitions.Views)
        {
            if (existing.Contains(view.Name)) blockedSources.Add(view.Name);
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var view in definitions.Views)
                if (blockedSources.Contains(view.ViewOn)) changed |= blockedSources.Add(view.Name);
        } while (changed);
        foreach (var view in definitions.Views)
        {
            var collision = existing.Contains(view.Name)
                ? DatabaseDefinitionCollision.Conflicting : DatabaseDefinitionCollision.None;
            items.Add(new(DatabaseDefinitionKind.View, view.Name, null, [view.ViewOn], collision,
                blockedSources.Contains(view.Name)
                    ? DatabaseDefinitionPlannedStatus.Blocked : DatabaseDefinitionPlannedStatus.Attempt));
        }

        return new DatabaseDefinitionImportPreview(items);
    }

    private static void ValidateConsistency(PreviewManifest manifest)
    {
        var definitions = manifest.Definitions!;
        if (!definitions.Collections.Select(item => item.Name).ToHashSet(StringComparer.Ordinal)
            .SetEquals(manifest.Collections!.Select(item => item.Name)))
            throw new ArgumentException("As coleções divergem das definições.");
        if (definitions.Views.Count != manifest.Views!.Count)
            throw new ArgumentException("As views divergem das definições.");
        foreach (var view in definitions.Views)
        {
            var legacy = manifest.Views.SingleOrDefault(item => string.Equals(item.Name, view.Name, StringComparison.Ordinal));
            if (legacy is null || !string.Equals(legacy.ViewOn, view.ViewOn, StringComparison.Ordinal)
                || !string.Equals("[" + string.Join(",", legacy.Pipeline) + "]", view.PipelineJson, StringComparison.Ordinal)
                || !string.Equals(legacy.Collation, view.CollationJson, StringComparison.Ordinal))
                throw new ArgumentException("Uma view diverge das definições.");
        }
    }

    private static bool IsDefaultIdIndex(IndexDefinitionSnapshot index)
    {
        if (!string.Equals(index.Name, "_id_", StringComparison.Ordinal)) return false;
        using var keys = JsonDocument.Parse(index.KeysJson);
        using var options = JsonDocument.Parse(index.OptionsJson);
        var fields = keys.RootElement.EnumerateObject().ToArray();
        var properties = options.RootElement.EnumerateObject().ToArray();
        return fields.Length == 1 && fields[0].Name == "_id" && fields[0].Value.ValueKind == JsonValueKind.Number
            && fields[0].Value.TryGetInt32(out var direction) && direction == 1
            && (properties.Length == 0 || properties.Length == 1 && properties[0].Name == "unique"
                && properties[0].Value.ValueKind == JsonValueKind.True);
    }

    private sealed record PreviewManifest(int FormatVersion,
        IReadOnlyList<PreviewCollection>? Collections, IReadOnlyList<PreviewView>? Views)
    {
        public DatabaseDefinitionManifest? Definitions { get; init; }
    }

    private sealed record PreviewCollection(string Name);
    private sealed record PreviewView(string Name, string ViewOn, IReadOnlyList<string> Pipeline, string? Collation);
}
