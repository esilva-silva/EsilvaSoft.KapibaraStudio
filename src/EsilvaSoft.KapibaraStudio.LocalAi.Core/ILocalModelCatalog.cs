using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public interface ILocalModelCatalog
{
    string DefaultDirectory { get; }
    /// <summary>Provides product-language validation messages; optional for headless callers.</summary>
    void SetLocalization(Func<string, string> localize) { }
    Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default);
    /// <summary>Each immediate subfolder of <paramref name="directory"/> is a candidate.</summary>
    Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(string directory, CancellationToken cancellationToken = default) => DiscoverAsync(cancellationToken);
    Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default);
}
