namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Resolves the transitive viewOn chain without reading collection documents.</summary>
public static class ViewDependencyChain
{
    public const int MaximumDepth = 20;

    public static async Task<ViewDependencyResolution> ResolveAsync(
        string view,
        string source,
        Func<string, CancellationToken, Task<ViewNamespaceDefinition?>> loadDefinition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(loadDefinition);

        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { view };
        var current = source;
        for (var depth = 0; depth < MaximumDepth; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(current))
            {
                return new ViewDependencyResolution(chain, CycleAt: current);
            }

            var definition = await loadDefinition(current, cancellationToken).ConfigureAwait(false);
            if (definition is null)
            {
                return new ViewDependencyResolution(chain, MissingAt: current);
            }

            chain.Add(current);
            if (!definition.IsView)
            {
                return new ViewDependencyResolution(chain);
            }

            if (string.IsNullOrWhiteSpace(definition.ViewOn))
            {
                return new ViewDependencyResolution(chain, MissingAt: current);
            }

            current = definition.ViewOn;
        }

        return new ViewDependencyResolution(chain, IsTruncated: true);
    }
}

public sealed record ViewNamespaceDefinition(bool IsView, string? ViewOn = null);

public sealed record ViewDependencyResolution(
    IReadOnlyList<string> Chain,
    string? CycleAt = null,
    string? MissingAt = null,
    bool IsTruncated = false)
{
    public bool IsValid => CycleAt is null && MissingAt is null && !IsTruncated;
}
