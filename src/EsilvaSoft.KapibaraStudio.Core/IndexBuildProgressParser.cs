using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Extracts only the index-build fields relevant to the requested namespace from a currentOp reply.</summary>
public static class IndexBuildProgressParser
{
    public static IReadOnlyList<IndexBuildProgress> Parse(string currentOperationsJson, string database, string collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        using var document = JsonDocument.Parse(currentOperationsJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("inprog", out var operations) || operations.ValueKind != JsonValueKind.Array)
            throw new JsonException("A resposta currentOp não contém a lista inprog esperada.");

        var namespaceName = $"{database}.{collection}";
        var results = new List<IndexBuildProgress>();
        foreach (var operation in operations.EnumerateArray())
        {
            if (operation.ValueKind != JsonValueKind.Object) continue;
            var command = operation.TryGetProperty("command", out var commandValue) && commandValue.ValueKind == JsonValueKind.Object
                ? commandValue : default;
            var createIndexesMatches = command.ValueKind == JsonValueKind.Object
                && ReadString(command, "createIndexes") is { } commandCollection
                && string.Equals(commandCollection, collection, StringComparison.Ordinal)
                && string.Equals(ReadString(command, "$db"), database, StringComparison.Ordinal);
            var namespaceMatches = string.Equals(ReadString(operation, "ns"), namespaceName, StringComparison.Ordinal);
            var message = ReadString(operation, "msg");
            var buildMessageMatches = message?.StartsWith("Index Build", StringComparison.OrdinalIgnoreCase) == true;
            if ((!createIndexesMatches && !buildMessageMatches) || (!namespaceMatches && !createIndexesMatches)) continue;

            var names = new List<string>();
            if (command.ValueKind == JsonValueKind.Object
                && command.TryGetProperty("indexes", out var indexArray) && indexArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var index in indexArray.EnumerateArray())
                {
                    if (index.ValueKind == JsonValueKind.Object && ReadString(index, "name") is { Length: > 0 } name)
                        names.Add(name);
                }
            }

            var progress = operation.TryGetProperty("progress", out var progressValue) && progressValue.ValueKind == JsonValueKind.Object
                ? progressValue : default;
            var done = progress.ValueKind == JsonValueKind.Object && progress.TryGetProperty("done", out var doneValue)
                ? ReadScalar(doneValue) : null;
            var total = progress.ValueKind == JsonValueKind.Object && progress.TryGetProperty("total", out var totalValue)
                ? ReadScalar(totalValue) : null;
            var quorumSpecified = false;
            string? quorum = null;
            if (command.ValueKind == JsonValueKind.Object && command.TryGetProperty("commitQuorum", out var quorumValue))
            {
                quorumSpecified = true;
                quorum = ReadScalar(quorumValue);
            }
            results.Add(new IndexBuildProgress(names, message, done, total, quorum, quorumSpecified));
        }

        return results;
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static string? ReadScalar(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var wrapper in new[] { "$numberInt", "$numberLong", "$numberDouble", "$numberDecimal" })
            {
                if (value.TryGetProperty(wrapper, out var wrapped)) return wrapped.ToString();
            }
        }
        return value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? value.ToString() : null;
    }
}

public sealed record IndexBuildProgress(
    IReadOnlyList<string> IndexNames,
    string? Message,
    string? Done,
    string? Total,
    string? ObservedCommitQuorum,
    bool CommitQuorumWasSpecified);
