using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Published model variants that can be installed as subfolders of the models directory.</summary>
public interface IRemoteModelSource
{
    IReadOnlyList<Uri> RepositoryUrls { get; }

    /// <summary>Receives the current UI localizer for diagnostics emitted by the source.</summary>
    void SetLocalization(Func<string, string> localize) { }

    /// <summary>Variants of every repository that answered; fails only when none could be listed.</summary>
    Task<IReadOnlyList<RemoteModelVariant>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and verifies every file, then installs the variant folder and returns its path. Never replaces an existing folder.</summary>
    Task<string> DownloadAsync(RemoteModelVariant variant, string modelsDirectory, IProgress<RemoteModelProgress>? progress, CancellationToken cancellationToken);
}
