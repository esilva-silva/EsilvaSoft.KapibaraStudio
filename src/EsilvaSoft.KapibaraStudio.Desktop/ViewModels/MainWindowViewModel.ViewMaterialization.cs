using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private sealed record ViewMaterializationPreview(
        ConnectionProfile Profile,
        string Database,
        string View,
        string Destination,
        string PipelineJson,
        string ViewDefinitionJson,
        string DestinationDefinitionJson)
    {
        public bool Matches(ConnectionProfile? profile, string? database, string? view, string? destination, string pipeline) =>
            ReferenceEquals(Profile, profile)
            && string.Equals(Database, database, StringComparison.Ordinal)
            && string.Equals(View, view, StringComparison.Ordinal)
            && string.Equals(Destination, destination, StringComparison.Ordinal)
            && string.Equals(PipelineJson, pipeline, StringComparison.Ordinal);
    }

    private ViewMaterializationPreview? _viewMaterializationPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMaterializeView))]
    [NotifyCanExecuteChangedFor(nameof(MaterializeViewCommand))]
    private string _materializationDestination = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMaterializeView))]
    [NotifyCanExecuteChangedFor(nameof(MaterializeViewCommand))]
    private string _materializationPipeline = "[]";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMaterializeView))]
    [NotifyCanExecuteChangedFor(nameof(MaterializeViewCommand))]
    private string _materializationConfirmation = string.Empty;

    [ObservableProperty]
    private string _materializationPreviewText = string.Empty;

    public bool CanMaterializeView => SelectedProfile is { IsReadOnly: false }
        && TryGetCollectionContext(out var profile, out var database, out var view)
        && _viewMaterializationPreview is { } preview
        && preview.Matches(profile, database, view, MaterializationDestination, MaterializationPipeline)
        && string.Equals(MaterializationDestination, MaterializationConfirmation.Trim(), StringComparison.Ordinal);

    [RelayCommand]
    private async Task PreviewViewMaterializationAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var view)
            || string.IsNullOrWhiteSpace(MaterializationDestination)
            || string.Equals(view, MaterializationDestination, StringComparison.Ordinal))
        {
            SetError(T("materializationTargetRequired"));
            return;
        }

        var destination = MaterializationDestination;
        var pipeline = MaterializationPipeline;
        _viewMaterializationPreview = null;
        MaterializationPreviewText = string.Empty;
        OnPropertyChanged(nameof(CanMaterializeView));
        MaterializeViewCommand.NotifyCanExecuteChanged();

        string? viewDefinition = null;
        string? destinationDefinition = null;
        string? destinationCount = null;
        if (!await RunAsync(async cancellationToken =>
            {
                viewDefinition = await _workspace.GetCollectionDefinitionAsync(profile, database, view, cancellationToken);
                destinationDefinition = await _workspace.GetCollectionDefinitionAsync(profile, database, destination, cancellationToken);
                if (destinationDefinition != "{}")
                {
                    try
                    {
                        var stats = await _workspace.GetCollectionStatsAsync(profile, database, destination, cancellationToken);
                        destinationCount = ReadCountFromStats(stats);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        destinationCount = null;
                    }
                }
            }))
        {
            return;
        }

        if (!IsOriginalCollectionContext(profile, database, view)
            || !string.Equals(destination, MaterializationDestination, StringComparison.Ordinal)
            || !string.Equals(pipeline, MaterializationPipeline, StringComparison.Ordinal))
        {
            return;
        }

        if (!IsExpectedNamespaceType(viewDefinition!, "view"))
        {
            SetError(T("selectedNamespaceIsNotView"));
            return;
        }

        if (destinationDefinition != "{}" && !IsExpectedNamespaceType(destinationDefinition!, "collection"))
        {
            SetError(T("materializationUnsupportedDestination"));
            return;
        }

        try
        {
            new ViewMaterializationRequest(
                database, view, destination, pipeline, destination, viewDefinition!, destinationDefinition!).Validate();
        }
        catch (ArgumentException exception)
        {
            SetError(exception.Message);
            return;
        }

        _viewMaterializationPreview = new ViewMaterializationPreview(
            profile, database, view, destination, pipeline, viewDefinition!, destinationDefinition!);
        MaterializationPreviewText = (destinationDefinition == "{}"
            ? F("materializationNewDestinationPreview", destination)
            : F("materializationExistingDestinationPreview", destination))
            + (destinationDefinition == "{}" ? string.Empty : Environment.NewLine
                + (destinationCount is null
                    ? T("materializationCountUnavailable")
                    : F("materializationDestinationCount", destinationCount)))
            + Environment.NewLine + T("materializationRaceWarning")
            + Environment.NewLine + T("materializationSourceDefinition")
            + Environment.NewLine + viewDefinition
            + Environment.NewLine + T("materializationDestinationDefinition")
            + Environment.NewLine + destinationDefinition;
        OnPropertyChanged(nameof(CanMaterializeView));
        MaterializeViewCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMaterializeView))]
    private async Task MaterializeViewAsync()
    {
        if (!CanMaterializeView
            || !TryGetCollectionContext(out var profile, out var database, out var view))
        {
            SetError(T("materializationPreviewRequired"));
            return;
        }

        var preview = _viewMaterializationPreview!;
        var request = new ViewMaterializationRequest(
            database,
            view,
            MaterializationDestination,
            MaterializationPipeline,
            MaterializationConfirmation,
            preview.ViewDefinitionJson,
            preview.DestinationDefinitionJson);
        ViewMaterializationResult? result = null;
        var submitted = false;
        var completed = await RunAsync(async cancellationToken =>
        {
            submitted = true;
            result = await _workspace.MaterializeViewAsync(profile, request, cancellationToken);
        });

        if (!completed)
        {
            if (submitted)
            {
                var effectMessage = T("materializationEffectUncertain");
                if (IsOriginalCollectionContext(profile, database, view))
                    StatusMessage = effectMessage;
                await RecordMaterializationAuditAsync(
                    "view.materialization.uncertain", profile, database, request.Destination,
                    T("materializationUncertainAudit"), effectMessage);
            }

            return;
        }

        _viewMaterializationPreview = null;
        MaterializationConfirmation = string.Empty;
        MaterializationPreviewText = string.Empty;
        OnPropertyChanged(nameof(CanMaterializeView));
        MaterializeViewCommand.NotifyCanExecuteChanged();
        var completedMessage = result!.ReplacedExisting
            ? F("materializationRefreshed", request.Destination)
            : F("materializationCreated", request.Destination);
        if (IsOriginalCollectionContext(profile, database, view))
            StatusMessage = completedMessage;
        await RecordMaterializationAuditAsync(
            "view.materialization", profile, database, request.Destination,
            T("materializationCompletedAudit"), completedMessage);
    }

    private async Task RecordMaterializationAuditAsync(
        string action,
        ConnectionProfile profile,
        string database,
        string destination,
        string summary,
        string effectMessage)
    {
        try
        {
            await _workspace.SaveAuditAsync(AuditEntry.Create(action, profile.Id, database, destination, summary));
        }
        catch (Exception exception)
        {
            StatusMessage = effectMessage + " " + F("auditNotRecorded", exception.Message);
        }
    }

    private static bool IsExpectedNamespaceType(string definitionJson, string expectedType)
    {
        try
        {
            using var json = JsonDocument.Parse(definitionJson);
            return json.RootElement.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), expectedType, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadCountFromStats(string statsJson)
    {
        try
        {
            using var json = JsonDocument.Parse(statsJson);
            if (!json.RootElement.TryGetProperty("count", out var count))
                return null;
            if (count.ValueKind == JsonValueKind.Number)
                return count.GetRawText();
            if (count.ValueKind == JsonValueKind.Object
                && count.TryGetProperty("$numberLong", out var longValue))
                return longValue.GetString();
        }
        catch (JsonException)
        {
            // The collision remains visible even when an older server returns unrecognized stats.
        }

        return null;
    }
}
