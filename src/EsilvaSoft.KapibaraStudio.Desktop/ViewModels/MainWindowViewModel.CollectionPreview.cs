using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CollectionRenameTargetPreview? _renameTargetPreview;

    [ObservableProperty]
    private string _renameTargetPreviewText = string.Empty;

    [ObservableProperty]
    private string _viewDependenciesText = string.Empty;

    private bool HasCurrentRenameTargetPreview() =>
        _renameTargetPreview is { } preview
        && preview.MatchesContext(SelectedProfile, SelectedDatabase, SelectedCollection, RenameCollectionName);

    [RelayCommand]
    private async Task PreviewRenameTargetAsync()
    {
        if (!RenameDropTarget
            || !TryGetCollectionContext(out var profile, out var database, out var source)
            || string.IsNullOrWhiteSpace(RenameCollectionName))
        {
            return;
        }

        var target = RenameCollectionName;
        _renameTargetPreview = null;
        RenameTargetPreviewText = string.Empty;
        OnPropertyChanged(nameof(CanRenameCollection));
        RenameCollectionCommand.NotifyCanExecuteChanged();
        string? definition = null;
        if (!await RunAsync(async cancellationToken =>
            definition = await _workspace.GetCollectionDefinitionAsync(profile, database, target, cancellationToken)))
        {
            return;
        }

        if (!IsOriginalCollectionContext(profile, database, source)
            || !RenameDropTarget
            || !string.Equals(RenameCollectionName, target, StringComparison.Ordinal))
        {
            return;
        }

        _renameTargetPreview = definition is null or "{}"
            ? null
            : new CollectionRenameTargetPreview(profile, database, source, target, definition);
        RenameTargetPreviewText = _renameTargetPreview is null
            ? F("renameTargetNotFound", target)
            : F("renameTargetWillBeRemoved", target) + Environment.NewLine + definition;
        OnPropertyChanged(nameof(CanRenameCollection));
        RenameCollectionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task LoadViewDependenciesAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var view))
        {
            return;
        }

        ViewDependencyResolution? dependencies = null;
        if (!await RunAsync(async cancellationToken =>
            {
                var viewJson = await _workspace.GetCollectionDefinitionAsync(profile, database, view, cancellationToken);
                var viewDefinition = ParseViewNamespaceDefinition(viewJson);
                if (viewDefinition is not { IsView: true, ViewOn: not null })
                {
                    throw new InvalidOperationException(T("selectedNamespaceIsNotView"));
                }

                dependencies = await ViewDependencyChain.ResolveAsync(
                    view,
                    viewDefinition.ViewOn,
                    async (name, token) => ParseViewNamespaceDefinition(
                        await _workspace.GetCollectionDefinitionAsync(profile, database, name, token)),
                    cancellationToken);
            }))
        {
            return;
        }

        if (!IsOriginalCollectionContext(profile, database, view))
        {
            return;
        }

        var chain = string.Join(" → ", new[] { view }.Concat(dependencies!.Chain));
        var detail = dependencies.CycleAt is { } cycle
            ? F("viewDependencyCycle", cycle)
            : dependencies.MissingAt is { } missing
                ? F("viewDependencyMissing", missing)
                : dependencies.IsTruncated
                    ? F("viewDependencyLimit", ViewDependencyChain.MaximumDepth)
                    : T("viewDependencyResolved");
        ViewDependenciesText = F("viewDependencyChain", chain) + Environment.NewLine
            + detail + Environment.NewLine + T("viewDependencyScope");
    }

    private static ViewNamespaceDefinition? ParseViewNamespaceDefinition(string definitionJson)
    {
        using var json = JsonDocument.Parse(definitionJson);
        var root = json.RootElement;
        if (!root.TryGetProperty("type", out var type))
        {
            return null;
        }

        var isView = string.Equals(type.GetString(), "view", StringComparison.Ordinal);
        if (!isView)
        {
            return new ViewNamespaceDefinition(false);
        }

        var viewOn = root.TryGetProperty("options", out var options)
            && options.TryGetProperty("viewOn", out var source)
            ? source.GetString()
            : null;
        return new ViewNamespaceDefinition(true, viewOn);
    }
}
