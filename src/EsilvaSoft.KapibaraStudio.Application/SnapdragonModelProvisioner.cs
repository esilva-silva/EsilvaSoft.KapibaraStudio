using System.Globalization;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Downloads both pinned packages once per directory/session, without changing settings or loading a model.</summary>
public sealed class SnapdragonModelProvisioner(ISnapdragonRuntimeAvailability availability, IRemoteModelSource source,
    IApplicationOperationService operations) : ISnapdragonModelProvisioner, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, SnapdragonProvisioningReport> _attempts = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private Func<string, string> _localize = static key => key;
    private int _disposed;

    public void SetLocalization(Func<string, string> localize) => _localize = localize ?? throw new ArgumentNullException(nameof(localize));
    private string L(string key, string fallback, params object?[] args)
    {
        var format = _localize(key);
        return string.Format(CultureInfo.CurrentCulture, format == key ? fallback : format, args);
    }

    public async Task<SnapdragonProvisioningReport> ProvisionAsync(string modelsDirectory, bool retry = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsDirectory);
        var directory = Path.GetFullPath(modelsDirectory);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!retry && _attempts.TryGetValue(directory, out var previous)) return previous;
            if (!await availability.IsAvailableAsync(linked.Token).ConfigureAwait(false))
                return _attempts[directory] = new(false, []);
            using var batch = operations.Begin(L("snapdragonProvisioning", "Preparando pacotes Snapdragon em {0}.", directory), ApplicationOperationPriority.Low, true, linked.Token);
            IReadOnlyList<RemoteModelVariant> variants;
            try { variants = await source.ListAsync(batch.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (batch.Token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                batch.Complete(ApplicationOperationStatus.Error, L("modelNotDownloaded", "Modelo não baixado: {0}", ex.Message));
                return _attempts[directory] = new(true, [], ex.Message);
            }
            var results = new List<SnapdragonProvisionedPackage>();
            foreach (var publication in SnapdragonModelPublications.Pinned)
            {
                batch.Token.ThrowIfCancellationRequested();
                var variant = variants.FirstOrDefault(item => item.Repository == publication.Repository && item.Revision == publication.Revision
                    && item.Variant == SnapdragonModelPublications.Folder && item.IsInstallable);
                using var operation = operations.Begin(L("downloadModelTo", "Baixando {0} em {1}.", variant?.Title ?? publication.Repository, directory), ApplicationOperationPriority.Normal, true, batch.Token);
                try
                {
                    if (variant is null) throw new InvalidDataException(L("snapdragonPublicationMissing", "Publicação Snapdragon fixada indisponível: {0}.", publication.Repository));
                    var progress = new InlineProgress(update => operation.Report(update.CompletedBytes, update.TotalBytes));
                    var path = await source.EnsureDownloadedAsync(variant, directory, progress, operation.Token).ConfigureAwait(false);
                    results.Add(new(publication.Repository, path, true, false, null));
                    operation.Complete(ApplicationOperationStatus.Success, L("modelDownloadedOperation", "Modelo baixado: {0}.", variant.Title));
                }
                catch (OperationCanceledException) when (batch.Token.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
                {
                    results.Add(new(publication.Repository, null, false, true, null));
                    operation.Complete(ApplicationOperationStatus.Cancelled, L("modelDownloadCancelled", "Download de modelo cancelado."));
                }
                catch (Exception ex)
                {
                    results.Add(new(publication.Repository, null, false, false, ex.Message));
                    operation.Complete(ApplicationOperationStatus.Error, L("modelNotDownloaded", "Modelo não baixado: {0}", ex.Message));
                }
            }
            var completed = results.Count(item => item.Completed);
            var errors = string.Join(" ", results.Where(item => item.Error is not null).Select(item => item.Repository + ": " + item.Error));
            batch.Complete(completed == results.Count ? ApplicationOperationStatus.Success : ApplicationOperationStatus.Warning,
                L("snapdragonProvisioningSummary", "Pacotes Snapdragon preparados: {0}/{1}. A seleção de modelo foi preservada.", completed, results.Count)
                    + (errors.Length == 0 ? "" : " " + errors));
            return _attempts[directory] = new(true, results);
        }
        finally
        {
            try { _gate.Release(); }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        _gate.Dispose();
        _shutdown.Dispose();
    }

    private sealed class InlineProgress(Action<RemoteModelProgress> report) : IProgress<RemoteModelProgress>
    {
        public void Report(RemoteModelProgress value) => report(value);
    }
}
