using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class AutocompleteSettingsViewModel
{
    public ObservableCollection<RemoteModelOption> RemoteModels { get; } = [];
    public bool HasRemoteSource => _remote is not null;
    public bool HasSelectedRemoteModel => SelectedRemoteModel is not null;
    public bool HasRemoteModelDetails => RemoteModelDetails.Length > 0;
    public bool HasDownloadStatus => DownloadStatus.Length > 0;
    public string DownloadPercent => $"{DownloadProgress:F0}%";

    /// <summary>Card of the source model, falling back to the folder of the export.</summary>
    public Uri? SelectedRemoteModelCardUrl => SelectedRemoteModel?.Variant.BaseModelUrl ?? SelectedRemoteModel?.Variant.PageUrl;

    public string RemoteSourceNotice => _remote is null ? ""
        : F("remoteSourceNotice", string.Join(", ", _remote.RepositoryUrls.Select(url => url.AbsolutePath.Trim('/'))));

    public string RemoteModelDetails => SelectedRemoteModel is not { } option ? ""
        : F("remoteModelDetails", option.Variant.License ?? T("licenseNotProvided"), option.IsInstalled ? T("alreadyInstalledAt") : T("installsAt"))
          + Path.Combine(EffectiveModelDirectory, option.Variant.FolderName);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RemoteModelDetails), nameof(HasRemoteModelDetails), nameof(HasSelectedRemoteModel), nameof(SelectedRemoteModelCardUrl))]
    [NotifyCanExecuteChangedFor(nameof(DownloadModelCommand))]
    private RemoteModelOption? _selectedRemoteModel;
    [ObservableProperty] private string _remoteModelsPlaceholder = T("remoteModelsNotLoaded");
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(DownloadModelCommand))] private bool _isDownloadingModel;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DownloadPercent))] private double _downloadProgress;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDownloadStatus))] private string _downloadStatus = "";

    partial void OnModelDirectoryChanged(string value) => RefreshInstalledRemoteModels();

    /// <summary>Fetches the published list the first time the window opens; later openings only recheck what is installed.</summary>
    public Task LoadRemoteModelsIfNeededAsync()
    {
        if (_remote is null || IsDownloadingModel) return Task.CompletedTask;
        if (RemoteModels.Count == 0) return LoadRemoteModelsCommand.ExecuteAsync(null);
        RefreshInstalledRemoteModels();
        return Task.CompletedTask;
    }

    /// <summary>Creates the effective models directory when missing, so the file manager can open it.</summary>
    public string? EnsureModelsDirectory()
    {
        var directory = EffectiveModelDirectory;
        if (directory.Length == 0)
        {
            OperationStatus = T("modelsDirectoryUndefined");
            return null;
        }
        try { return Directory.CreateDirectory(directory).FullName; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            OperationStatus = F("createDirectoryFailed", directory, ex.Message);
            return null;
        }
    }

    internal static string FormatSize(long bytes) => bytes >= 1_000_000_000
        ? string.Create(CultureInfo.CurrentCulture, $"{bytes / 1_000_000_000d:0.##} GB")
        : string.Create(CultureInfo.CurrentCulture, $"{Math.Max(1, Math.Round(bytes / 1_000_000d)):0} MB");

    [RelayCommand]
    private async Task LoadRemoteModelsAsync(CancellationToken cancellationToken)
    {
        if (_remote is null) return;
        RemoteModelsPlaceholder = T("remoteModelsLoading");
        try
        {
            var variants = await _remote.ListAsync(cancellationToken);
            ShowRemoteModels(variants, SelectedRemoteModel?.Variant);
            RemoteModelsPlaceholder = variants.Count == 0 ? T("remoteModelsNone") : T("remoteModelsChoose");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { RemoteModelsPlaceholder = T("remoteModelsNotLoaded"); }
        catch (Exception) { RemoteModelsPlaceholder = T("remoteModelsUnavailable"); }
    }

    private bool CanDownloadModel() => _remote is not null && SelectedRemoteModel is { IsInstalled: false } && !IsDownloadingModel;

    [RelayCommand(CanExecute = nameof(CanDownloadModel), IncludeCancelCommand = true)]
    private async Task DownloadModelAsync(CancellationToken cancellationToken)
    {
        if (_remote is not { } remote || SelectedRemoteModel is not { IsInstalled: false } option) return;
        var variant = option.Variant;
        var directory = EffectiveModelDirectory;
        IsDownloadingModel = true;
        DownloadProgress = 0;
        DownloadStatus = F("downloadModelTo", variant.Title, directory);
        using var operation = _operations?.Begin(F("downloadModelTo", variant.Title, directory), ApplicationOperationPriority.Normal, canCancel: true, cancellationToken);
        var token = operation?.Token ?? cancellationToken;
        var progress = new Progress<RemoteModelProgress>(update =>
        {
            if (!IsDownloadingModel) return;
            DownloadProgress = update.TotalBytes > 0 ? Math.Clamp(100d * update.CompletedBytes / update.TotalBytes, 0, 100) : 0;
            DownloadStatus = F("downloadModelProgress", variant.Title, FormatSize(update.CompletedBytes), FormatSize(update.TotalBytes));
            operation?.Report(update.CompletedBytes, update.TotalBytes);
        });
        try
        {
            // Hashing gigabytes stays off the UI thread; Progress<T> marshals updates back.
            var path = await Task.Run(() => remote.DownloadAsync(variant, directory, progress, token), CancellationToken.None);
            operation?.Complete(ApplicationOperationStatus.Success, F("modelDownloadedOperation", variant.Title));
            IsDownloadingModel = false;
            DownloadProgress = 100;
            await RefreshModelsCommand.ExecuteAsync(null);
            if (Models.FirstOrDefault(model => !model.IsExternal && string.Equals(model.Reference, variant.FolderName, PathComparison)) is { } installed)
            {
                SelectedModelOption = installed;
                DownloadStatus = F("modelInstalledSave", variant.Title, path);
            }
            else DownloadStatus = F("modelCatalogMissed", path);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            operation?.Complete(ApplicationOperationStatus.Cancelled, T("modelDownloadCancelled"));
            DownloadStatus = T("downloadCancelledResume");
        }
        catch (Exception ex)
        {
            operation?.Complete(ApplicationOperationStatus.Error, F("modelNotDownloaded", ex.Message));
            DownloadStatus = F("downloadIncomplete", ex.Message);
        }
        finally
        {
            IsDownloadingModel = false;
            RefreshInstalledRemoteModels();
        }
    }

    private void RefreshInstalledRemoteModels()
    {
        if (RemoteModels.Count > 0) ShowRemoteModels(RemoteModels.Select(option => option.Variant).ToArray(), SelectedRemoteModel?.Variant);
    }

    private void ShowRemoteModels(IEnumerable<RemoteModelVariant> variants, RemoteModelVariant? selected)
    {
        var directory = EffectiveModelDirectory;
        // Only a completed detection that found no usable GPU marks GPU exports; an unknown state never warns.
        var gpuMissing = _hardware.Count > 0 && !_hardware.Any(device => device.Kind == AiAccelerationMode.Gpu && device.IsAvailable);
        var options = variants.Select(variant => new RemoteModelOption(variant, IsInstalled(directory, variant),
            gpuMissing && variant.Flavor?.Hardware == AiAccelerationMode.Gpu)).ToArray();
        RemoteModels.Clear();
        foreach (var option in options) RemoteModels.Add(option);
        SelectedRemoteModel = options.FirstOrDefault(option => selected is not null && option.Variant.FolderName == selected.FolderName)
            ?? options.FirstOrDefault(option => !option.IsInstalled && !option.HardwareMissing) ?? options.FirstOrDefault();
    }

    private static bool IsInstalled(string directory, RemoteModelVariant variant)
    {
        if (directory.Length == 0) return false;
        try { return Directory.Exists(Path.Combine(directory, variant.FolderName)); }
        catch (ArgumentException) { return false; }
    }
}
