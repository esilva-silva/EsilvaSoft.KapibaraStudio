namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>A model publication pinned to one commit; installable variants are self-contained ONNX GenAI exports.</summary>
public sealed record RemoteModelVariant(string Repository, string Revision, string Variant, string FolderName, long SizeBytes, string? License,
    Uri PageUrl, IReadOnlyList<RemoteModelFile> Files)
{
    /// <summary>Whether this publication can be downloaded into the models directory; does not imply runtime compatibility.</summary>
    public bool IsInstallable { get; init; } = true;

    /// <summary>Download is allowed, but execution requires a runtime not supported by this IDE version.</summary>
    public bool RequiresUnsupportedRuntime { get; init; }

    /// <summary>Model family shown to the user, such as KapiCoder-Mongo-Small; empty when unknown.</summary>
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
