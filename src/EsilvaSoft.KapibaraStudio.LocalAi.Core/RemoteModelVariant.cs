namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>A self-contained ONNX GenAI export published as a top-level folder of a model repository, pinned to one commit.</summary>
public sealed record RemoteModelVariant(string Repository, string Revision, string Variant, string FolderName, long SizeBytes, string? License,
    Uri PageUrl, IReadOnlyList<RemoteModelFile> Files)
{
    /// <summary>Model family shown to the user, such as SlopCoder-Mongo-1.5B-full; empty when unknown.</summary>
    public string Family { get; init; } = "";

    /// <summary>When to choose this variant, as stated by the publisher.</summary>
    public string? Hint { get; init; }

    /// <summary>Optional catalog key for a built-in publisher hint; custom sources may leave this empty.</summary>
    public string? HintKey { get; init; }

    /// <summary>Card of the source transformers model: prompt format, training data and limitations.</summary>
    public Uri? BaseModelUrl { get; init; }

    public ModelFlavor? Flavor => ModelDisplayNames.ParseVariant(Variant);

    public string Title => Family.Length == 0 ? FolderName
        : Flavor is { } flavor ? ModelDisplayNames.Title(Family, flavor) : $"{Family} — {Variant}";
}
