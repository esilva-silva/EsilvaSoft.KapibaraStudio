using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private sealed record ProfilerConfigurationPreview(
        ConnectionProfile Profile,
        string Database,
        string Level,
        string SlowMs,
        string SampleRate,
        bool UseFilter,
        string FilterJson,
        string StatusJson,
        string TopologyJson)
    {
        public bool Matches(ConnectionProfile? profile, string? database, string level, string slowMs,
            string sampleRate, bool useFilter, string filterJson) =>
            ReferenceEquals(Profile, profile)
            && string.Equals(Database, database, StringComparison.Ordinal)
            && string.Equals(Level, level, StringComparison.Ordinal)
            && string.Equals(SlowMs, slowMs, StringComparison.Ordinal)
            && string.Equals(SampleRate, sampleRate, StringComparison.Ordinal)
            && UseFilter == useFilter
            && string.Equals(FilterJson, filterJson, StringComparison.Ordinal);
    }

    private ProfilerConfigurationPreview? _profilerConfigurationPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureProfiler))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureProfilerCommand))]
    private string _profilerLevelText = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureProfiler))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureProfilerCommand))]
    private string _profilerSlowMsText = "100";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureProfiler))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureProfilerCommand))]
    private string _profilerSampleRateText = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureProfiler))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureProfilerCommand))]
    private bool _profilerUseFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureProfiler))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureProfilerCommand))]
    private string _profilerFilterJson = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureProfiler))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureProfilerCommand))]
    private string _profilerConfirmation = string.Empty;

    [ObservableProperty]
    private string _profilerConfigurationPreviewText = string.Empty;

    public bool CanConfigureProfiler => SelectedProfile is { IsReadOnly: false } profile
        && _profilerConfigurationPreview is { } preview
        && preview.Matches(profile, SelectedDatabase, ProfilerLevelText, ProfilerSlowMsText,
            ProfilerSampleRateText, ProfilerUseFilter, ProfilerFilterJson)
        && string.Equals(preview.Database, ProfilerConfirmation?.Trim(), StringComparison.Ordinal);

    private void NotifyProfilerTargetChanged()
    {
        OnPropertyChanged(nameof(CanConfigureProfiler));
        ConfigureProfilerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task PreviewProfilerConfigurationAsync()
    {
        var profile = SelectedProfile;
        var database = SelectedDatabase;
        if (profile is null || string.IsNullOrWhiteSpace(database))
            return;

        var level = ProfilerLevelText;
        var slowMs = ProfilerSlowMsText;
        var sampleRate = ProfilerSampleRateText;
        var useFilter = ProfilerUseFilter;
        var filter = ProfilerFilterJson;
        _profilerConfigurationPreview = null;
        ProfilerConfigurationPreviewText = string.Empty;
        ConfigureProfilerCommand.NotifyCanExecuteChanged();
        string? status = null;
        string? topology = null;
        if (!await RunAsync(async cancellationToken =>
            {
                status = await _workspace.GetProfilerStatusAsync(profile, database, cancellationToken);
                topology = await _workspace.GetTopologyAsync(profile, cancellationToken);
            }))
            return;
        if (!ReferenceEquals(SelectedProfile, profile)
            || !string.Equals(SelectedDatabase, database, StringComparison.Ordinal)
            || !string.Equals(level, ProfilerLevelText, StringComparison.Ordinal)
            || !string.Equals(slowMs, ProfilerSlowMsText, StringComparison.Ordinal)
            || !string.Equals(sampleRate, ProfilerSampleRateText, StringComparison.Ordinal)
            || useFilter != ProfilerUseFilter
            || !string.Equals(filter, ProfilerFilterJson, StringComparison.Ordinal))
            return;

        try
        {
            var request = BuildProfilerRequest(database, level, slowMs, sampleRate, useFilter,
                filter, database, status!, topology!).Validate();
            var current = ProfilerSettings.Parse(status!);
            _profilerConfigurationPreview = new ProfilerConfigurationPreview(
                profile, database, level, slowMs, sampleRate, useFilter, filter, status!, topology!);
            ProfilerConfigurationPreviewText = F("profilerPreviewTarget", database)
                + Environment.NewLine + F("profilerPreviewCurrent", current.Level,
                    current.SlowMs?.ToString(CultureInfo.InvariantCulture) ?? "?",
                    current.SampleRate?.ToString(CultureInfo.InvariantCulture) ?? "?",
                    current.FilterDigest is null ? T("no") : T("yes"))
                + Environment.NewLine + F("profilerPreviewProposed", request.Level,
                    request.SlowMs?.ToString(CultureInfo.InvariantCulture) ?? "—",
                    request.SampleRate?.ToString(CultureInfo.InvariantCulture) ?? "—",
                    request.FilterMode == ProfilerFilterMode.Set ? T("yes") : T("no"))
                + Environment.NewLine + (request.Level == 2 ? T("profilerLevel2Risk")
                    : request.FilterMode == ProfilerFilterMode.Set ? T("profilerFilterRisk")
                    : request.Level == 0 ? T("profilerLevel0Scope") : T("profilerThresholdRisk"))
                + Environment.NewLine + T("profilerConcurrentWarning");
            OnPropertyChanged(nameof(CanConfigureProfiler));
            ConfigureProfilerCommand.NotifyCanExecuteChanged();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or JsonException)
        {
            SetError(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfigureProfiler))]
    private async Task ConfigureProfilerAsync()
    {
        if (!CanConfigureProfiler || SelectedProfile is not { } profile
            || _profilerConfigurationPreview is not { } preview)
        {
            SetError(T("profilerPreviewRequired"));
            return;
        }

        var request = BuildProfilerRequest(preview.Database, preview.Level, preview.SlowMs,
            preview.SampleRate, preview.UseFilter, preview.FilterJson,
            ProfilerConfirmation, preview.StatusJson, preview.TopologyJson);
        ProfilerConfigurationResult? result = null;
        var submitted = false;
        var completed = await RunAsync(async cancellationToken =>
        {
            submitted = true;
            result = await _workspace.ConfigureProfilerAsync(profile, request, cancellationToken);
        });
        if (!completed)
        {
            if (submitted)
            {
                if (ReferenceEquals(SelectedProfile, profile))
                    StatusMessage = T("profilerEffectUncertain");
                await RecordProfilerAuditAsync(profile, request.Database, "profiler.configure.uncertain",
                    T("profilerEffectUncertain"));
            }
            return;
        }

        _profilerConfigurationPreview = null;
        ProfilerConfirmation = string.Empty;
        ProfilerConfigurationPreviewText = string.Empty;
        OnPropertyChanged(nameof(CanConfigureProfiler));
        ConfigureProfilerCommand.NotifyCanExecuteChanged();
        if (ReferenceEquals(SelectedProfile, profile)
            && string.Equals(SelectedDatabase, request.Database, StringComparison.Ordinal))
            StatusMessage = F("profilerConfigured", request.Database, result!.Level);
        await RecordProfilerAuditAsync(profile, request.Database, "profiler.configure",
            F("profilerConfigured", request.Database, result!.Level));
    }

    private async Task RecordProfilerAuditAsync(ConnectionProfile profile, string database, string action, string summary)
    {
        try
        {
            await _workspace.SaveAuditAsync(AuditEntry.Create(action, profile.Id, database, null, summary));
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(SelectedProfile, profile)
                && string.Equals(SelectedDatabase, database, StringComparison.Ordinal))
                StatusMessage = summary + " " + F("auditNotRecorded", exception.Message);
        }
    }

    private static ProfilerConfigurationRequest BuildProfilerRequest(
        string database, string level, string slowMs, string sampleRate, bool useFilter,
        string filterJson, string confirmation, string statusJson, string topologyJson)
    {
        if (!int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedLevel))
            throw new ArgumentException("O nível do profiler deve ser 0, 1 ou 2.", nameof(level));
        int? parsedSlow = null;
        if (!string.IsNullOrWhiteSpace(slowMs))
        {
            if (!int.TryParse(slowMs, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException("slowms deve ser um inteiro positivo.", nameof(slowMs));
            parsedSlow = value;
        }

        decimal? parsedRate = null;
        if (!string.IsNullOrWhiteSpace(sampleRate))
        {
            if (!decimal.TryParse(sampleRate, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException("sampleRate deve ser um decimal entre 0 e 1, usando ponto.", nameof(sampleRate));
            parsedRate = value;
        }

        return new ProfilerConfigurationRequest(database, parsedLevel, parsedSlow, parsedRate,
            useFilter ? ProfilerFilterMode.Set : ProfilerFilterMode.Unset,
            filterJson, confirmation, statusJson, topologyJson);
    }
}
