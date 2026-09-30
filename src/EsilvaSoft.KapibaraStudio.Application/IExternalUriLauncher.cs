namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Opens an explicitly requested web address in the platform default browser.</summary>
public interface IExternalUriLauncher
{
    /// <summary>Submits a web address to the browser; no credential material is returned or persisted.</summary>
    /// <param name="uri">Validated absolute HTTP or HTTPS destination.</param>
    /// <param name="cancellationToken">Cancellation checked before submitting the launch.</param>
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}
