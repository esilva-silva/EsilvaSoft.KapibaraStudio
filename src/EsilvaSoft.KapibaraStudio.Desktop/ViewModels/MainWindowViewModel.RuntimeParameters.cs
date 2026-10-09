using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyRuntimeParameter))]
    [NotifyCanExecuteChangedFor(nameof(ApplyRuntimeParameterCommand))]
    private string _runtimeParameterName = RuntimeServerParameters.LogLevel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyRuntimeParameter))]
    [NotifyCanExecuteChangedFor(nameof(ApplyRuntimeParameterCommand))]
    private decimal? _runtimeParameterValue = RuntimeServerParameters.LogLevelMinimum;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyRuntimeParameter))]
    [NotifyCanExecuteChangedFor(nameof(ApplyRuntimeParameterCommand))]
    private string _runtimeParameterConfirmation = string.Empty;

    [ObservableProperty]
    private string _runtimeParameterPreview = string.Empty;

    private ConnectionProfile? _runtimeParameterPreviewProfile;
    private int? _runtimeParameterPreviousValue;
    private int? _runtimeParameterRequestedValue;
    private string? _runtimeParameterPreviewName;

    public decimal RuntimeParameterMinimum => RuntimeServerParameters.GetRequestedValueBounds(RuntimeParameterName).Minimum;
    public decimal RuntimeParameterMaximum => RuntimeServerParameters.GetRequestedValueBounds(RuntimeParameterName).Maximum;

    public bool CanApplyRuntimeParameter => SelectedProfile is { IsReadOnly: false } profile
        && ReferenceEquals(profile, _runtimeParameterPreviewProfile)
        && string.Equals(RuntimeParameterName, _runtimeParameterPreviewName, StringComparison.Ordinal)
        && _runtimeParameterPreviousValue is not null
        && _runtimeParameterRequestedValue is not null
        && RuntimeParameterValue is decimal requested
        && requested == _runtimeParameterRequestedValue.Value
        && string.Equals(RuntimeParameterName, RuntimeParameterConfirmation.Trim(), StringComparison.Ordinal);

    partial void OnRuntimeParameterNameChanged(string value)
    {
        ClearRuntimeParameterPreview();
        OnPropertyChanged(nameof(RuntimeParameterMinimum));
        OnPropertyChanged(nameof(RuntimeParameterMaximum));
        if (RuntimeParameterValue is not decimal current || current < RuntimeParameterMinimum || current > RuntimeParameterMaximum)
            RuntimeParameterValue = RuntimeParameterMinimum;
    }

    [RelayCommand]
    private async Task PreviewRuntimeParameterAsync()
    {
        var profile = SelectedProfile;
        var parameterName = RuntimeParameterName;
        if (profile is null || !TryGetRequestedRuntimeParameter(parameterName, out var requested))
        {
            SetError(T("runtimeParameterValueInvalid"));
            return;
        }

        ClearRuntimeParameterPreview();
        if (!await RunAsync(async cancellationToken =>
            {
                var current = await _workspace.GetRuntimeServerParameterAsync(profile, parameterName, cancellationToken);
                if (!ReferenceEquals(profile, SelectedProfile)
                    || !string.Equals(parameterName, RuntimeParameterName, StringComparison.Ordinal)
                    || !TryGetRequestedRuntimeParameter(parameterName, out var currentRequest)
                    || currentRequest != requested)
                    return;

                _runtimeParameterPreviewProfile = profile;
                _runtimeParameterPreviewName = parameterName;
                _runtimeParameterPreviousValue = current;
                _runtimeParameterRequestedValue = requested;
                RuntimeParameterPreview = FormatRuntimeParameterDiff(current, requested, DateTimeOffset.UtcNow);
                OnPropertyChanged(nameof(CanApplyRuntimeParameter));
                ApplyRuntimeParameterCommand.NotifyCanExecuteChanged();
                StatusMessage = T("runtimeParameterPreviewReady");
            }))
        {
            ClearRuntimeParameterPreview();
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyRuntimeParameter))]
    private async Task ApplyRuntimeParameterAsync()
    {
        var profile = SelectedProfile;
        if (profile is null
            || !CanApplyRuntimeParameter
            || _runtimeParameterPreviewName is not { } parameterName
            || _runtimeParameterPreviousValue is not int previous
            || _runtimeParameterRequestedValue is not int requested)
            return;

        var request = new RuntimeServerParameterRequest(
            parameterName,
            previous,
            requested,
            RuntimeParameterConfirmation);
        if (!await RunAsync(async cancellationToken =>
            {
                var result = await _workspace.SetRuntimeServerParameterAsync(profile, request, cancellationToken);
                if (ReferenceEquals(profile, SelectedProfile) && string.Equals(parameterName, RuntimeParameterName, StringComparison.Ordinal))
                    RuntimeParameterPreview = FormatRuntimeParameterDiff(result.PreviousValue, result.CurrentValue, result.ChangedAtUtc);
            }))
            return;

        await RecordAuditAsync(
            "server.parameter.runtime",
            profile,
            "admin",
            null,
            F("runtimeParameterAudit", parameterName, previous, requested));
        StatusMessage = F("runtimeParameterChanged", parameterName, requested);
        ClearRuntimeParameterPreview();
        RuntimeParameterConfirmation = string.Empty;
    }

    private bool TryGetRequestedRuntimeParameter(string parameterName, out int value)
    {
        value = 0;
        var (minimum, maximum) = RuntimeServerParameters.GetRequestedValueBounds(parameterName);
        var candidate = RuntimeParameterValue;
        if (candidate is null || candidate < minimum || candidate > maximum || decimal.Truncate(candidate.Value) != candidate.Value)
            return false;

        value = decimal.ToInt32(candidate.Value);
        return true;
    }

    private static string FormatRuntimeParameterDiff(int previous, int requested, DateTimeOffset observedAt) =>
        string.Join(Environment.NewLine,
            F("runtimeParameterBefore", previous),
            F("runtimeParameterAfter", requested),
            F("runtimeParameterObservedAt", observedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture)),
            T("runtimeParameterEphemeralNote"));

    private void ClearRuntimeParameterPreview()
    {
        _runtimeParameterPreviewProfile = null;
        _runtimeParameterPreviewName = null;
        _runtimeParameterPreviousValue = null;
        _runtimeParameterRequestedValue = null;
        RuntimeParameterPreview = string.Empty;
        OnPropertyChanged(nameof(CanApplyRuntimeParameter));
        ApplyRuntimeParameterCommand.NotifyCanExecuteChanged();
    }

    private void NotifyRuntimeParameterTargetChanged() => ClearRuntimeParameterPreview();
}
