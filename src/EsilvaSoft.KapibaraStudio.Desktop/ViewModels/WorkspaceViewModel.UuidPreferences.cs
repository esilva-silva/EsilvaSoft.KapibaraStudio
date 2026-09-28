using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    [ObservableProperty] private UuidRepresentation _uuidRepresentation = UuidRepresentation.Standard;
    [ObservableProperty] private IdentifierRepresentationMode _identifierMode = IdentifierRepresentationMode.Standard;

    public UuidDisplayPolicy CaptureUuidPolicy() => new(UuidRepresentation, _profileUuidRepresentations, IdentifierMode);

    /// <summary>Applies the global identifier mode to idle tabs and persists it. The UUID representation is not touched.</summary>
    public async Task SetIdentifierModeAsync(IdentifierRepresentationMode value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), "Modo de identificador desconhecido.");
        IdentifierMode = value;
        await SaveSessionAsync();
    }

    partial void OnIdentifierModeChanged(IdentifierRepresentationMode value)
    {
        UuidPreferences.IsPreviewVisible = value != IdentifierRepresentationMode.ObjectId;
        PublishUuidPolicy();
    }
    public UuidRepresentation? GetProfileUuidRepresentation(Guid? profileId) =>
        profileId is { } id && _profileUuidRepresentations.TryGetValue(id, out var value) ? value : null;

    /// <summary>Applies the global preference to idle tabs immediately and persists it; a failed save stays visible and in memory.</summary>
    public async Task SetUuidRepresentationAsync(UuidRepresentation value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), "Representação UUID desconhecida.");
        UuidRepresentation = value;
        await SaveSessionAsync();
    }

    public async Task SetProfileUuidRepresentationAsync(Guid profileId, UuidRepresentation? value)
    {
        if (value is { } defined && !Enum.IsDefined(defined)) throw new ArgumentOutOfRangeException(nameof(value), "Representação UUID desconhecida.");
        if (GetProfileUuidRepresentation(profileId) == value) return;
        if (value is null) _profileUuidRepresentations.Remove(profileId); else _profileUuidRepresentations[profileId] = value.Value;
        PublishUuidPolicy();
        await SaveSessionAsync();
    }

    private void PublishUuidPolicy()
    {
        var policy = CaptureUuidPolicy();
        foreach (var tab in Tabs) tab.UuidPolicy = policy;
    }

    partial void OnUuidRepresentationChanged(UuidRepresentation value)
    {
        PublishUuidPolicy();
        IdentifierPreferences?.RefreshPreview();
    }
}
