namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>A repository of ONNX GenAI exports and the transformers repository it was exported from.</summary>
public sealed record RemoteModelRepository(string Repository, string? BaseRepository, IReadOnlyList<RemoteModelVariantHint> Variants)
{
    /// <summary>Optional immutable hub commit. Null follows the repository's current default branch.</summary>
    public string? Revision { get; init; }

    /// <summary>Optional single variant folder to list at the pinned revision.</summary>
    public string? VariantFolder { get; init; }

    /// <summary>False keeps a publication visible while its runtime is not supported.</summary>
    public bool IsInstallable { get; init; } = true;

    /// <summary>Keep download availability separate from compatibility of the IDE runtime.</summary>
    public bool RequiresUnsupportedRuntime { get; init; }
}
