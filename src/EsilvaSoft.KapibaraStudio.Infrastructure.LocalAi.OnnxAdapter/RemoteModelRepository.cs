namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>A repository of ONNX GenAI exports and the transformers repository it was exported from.</summary>
public sealed record RemoteModelRepository(string Repository, string? BaseRepository, IReadOnlyList<RemoteModelVariantHint> Variants);
