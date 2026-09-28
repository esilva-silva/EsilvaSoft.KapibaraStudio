namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public sealed record LocalModelValidation(LocalModelDefinition? Model, LocalModelStatus Status)
{
    /// <summary>Folder that was checked, including invalid candidates without a definition.</summary>
    public string Path { get; init; } = Model?.Path ?? "";
    public LocalModelValidity Validity => Status.State switch
    {
        LocalModelState.Available or LocalModelState.Loading or LocalModelState.Ready => LocalModelValidity.Valid,
        LocalModelState.Unsupported => LocalModelValidity.Unsupported,
        LocalModelState.MissingFiles or LocalModelState.NotInstalled => LocalModelValidity.MissingFiles,
        _ => LocalModelValidity.Invalid
    };
}
