using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewCustomRole), nameof(CanApplyCustomRole))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCreateCustomRoleCommand), nameof(PreviewUpdateCustomRoleCommand), nameof(PreviewDropCustomRoleCommand), nameof(ApplyCustomRoleCommand))]
    private string _customRoleName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewCustomRole), nameof(CanApplyCustomRole))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCreateCustomRoleCommand), nameof(PreviewUpdateCustomRoleCommand), nameof(PreviewDropCustomRoleCommand), nameof(ApplyCustomRoleCommand))]
    private string _customRolePrivileges = "[]";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewCustomRole), nameof(CanApplyCustomRole))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCreateCustomRoleCommand), nameof(PreviewUpdateCustomRoleCommand), nameof(PreviewDropCustomRoleCommand), nameof(ApplyCustomRoleCommand))]
    private string _customRoleInheritedRoles = "[]";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyCustomRole))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCustomRoleCommand))]
    private string _customRoleConfirmation = string.Empty;

    [ObservableProperty]
    private string _customRolePreview = string.Empty;

    private string? _customRoleExpectedDefinition;
    private string? _customRolePreviewSignature;
    private DatabaseRoleMutationKind _customRolePreviewKind;

    public bool CanPreviewCustomRole => SelectedProfile is not null
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(CustomRoleName)
        && CustomRoleName.Trim().Length <= 128;

    public bool CanApplyCustomRole => SelectedProfile is { IsReadOnly: false }
        && _customRoleExpectedDefinition is not null
        && string.Equals(_customRolePreviewSignature, CurrentCustomRoleSignature(_customRolePreviewKind), StringComparison.Ordinal)
        && string.Equals(CustomRoleName.Trim(), CustomRoleConfirmation.Trim(), StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanPreviewCustomRole))]
    private Task PreviewCreateCustomRoleAsync() => PreviewCustomRoleAsync(DatabaseRoleMutationKind.Create);

    [RelayCommand(CanExecute = nameof(CanPreviewCustomRole))]
    private Task PreviewUpdateCustomRoleAsync() => PreviewCustomRoleAsync(DatabaseRoleMutationKind.Update);

    [RelayCommand(CanExecute = nameof(CanPreviewCustomRole))]
    private Task PreviewDropCustomRoleAsync() => PreviewCustomRoleAsync(DatabaseRoleMutationKind.Drop);

    [RelayCommand(CanExecute = nameof(CanApplyCustomRole))]
    private async Task ApplyCustomRoleAsync()
    {
        var profile = SelectedProfile;
        var database = SelectedDatabase;
        var expectedDefinition = _customRoleExpectedDefinition;
        if (profile is null || string.IsNullOrWhiteSpace(database) || expectedDefinition is null || !CanApplyCustomRole)
            return;

        var request = CreateCustomRoleRequest(database, _customRolePreviewKind, expectedDefinition);
        if (!await RunRoleAdministrationAsync(async cancellationToken =>
            {
                var result = await _workspace.MutateCustomRoleAsync(profile, request, cancellationToken);
                CustomRolePreview = FormatRoleDiff(result.Before, result.After);
            }))
            return;

        var action = request.Kind switch
        {
            DatabaseRoleMutationKind.Create => "role.create",
            DatabaseRoleMutationKind.Update => "role.update",
            _ => "role.drop"
        };
        StatusMessage = F("customRoleMutationCompleted", request.RoleName.Trim());
        await RecordSensitiveAdministrationAuditAsync(action, profile, database, T("auditCustomRoleChanged"));
        ClearCustomRolePreview();
        CustomRoleConfirmation = string.Empty;
    }

    private async Task PreviewCustomRoleAsync(DatabaseRoleMutationKind kind)
    {
        var profile = SelectedProfile;
        var database = SelectedDatabase;
        var roleName = CustomRoleName.Trim();
        var privilegesJson = CustomRolePrivileges;
        var inheritedRolesJson = CustomRoleInheritedRoles;
        if (profile is null || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(roleName))
            return;

        ClearCustomRolePreview();
        var signature = BuildCustomRoleSignature(profile, database, roleName, kind, privilegesJson, inheritedRolesJson);
        if (!await RunRoleAdministrationAsync(async cancellationToken =>
            {
                var currentJson = await _workspace.GetCustomRoleDefinitionAsync(profile, database, roleName, cancellationToken);
                if (!ReferenceEquals(profile, SelectedProfile)
                    || !string.Equals(database, SelectedDatabase, StringComparison.Ordinal)
                    || !string.Equals(roleName, CustomRoleName.Trim(), StringComparison.Ordinal)
                    || !string.Equals(privilegesJson, CustomRolePrivileges, StringComparison.Ordinal)
                    || !string.Equals(inheritedRolesJson, CustomRoleInheritedRoles, StringComparison.Ordinal)
                    || !string.Equals(signature, CurrentCustomRoleSignature(kind), StringComparison.Ordinal))
                    return;

                using var current = JsonDocument.Parse(currentJson);
                var exists = current.RootElement.ValueKind == JsonValueKind.Object;
                if (exists && current.RootElement.TryGetProperty("isBuiltin", out var builtin) && builtin.ValueKind == JsonValueKind.True)
                    throw new InvalidOperationException(T("customRoleBuiltinProtected"));
                if (kind == DatabaseRoleMutationKind.Create && exists)
                    throw new InvalidOperationException(T("customRoleAlreadyExists"));
                if ((kind is DatabaseRoleMutationKind.Update or DatabaseRoleMutationKind.Drop) && !exists)
                    throw new InvalidOperationException(T("customRoleMissing"));

                var desiredJson = kind == DatabaseRoleMutationKind.Drop
                    ? null
                    : BuildDesiredCustomRoleJson(database, roleName, privilegesJson, inheritedRolesJson);
                CustomRolePreview = FormatRoleDiff(exists ? currentJson : null, desiredJson);
                _customRoleExpectedDefinition = currentJson;
                _customRolePreviewKind = kind;
                _customRolePreviewSignature = signature;
                OnPropertyChanged(nameof(CanApplyCustomRole));
                ApplyCustomRoleCommand.NotifyCanExecuteChanged();
            }))
            ClearCustomRolePreview();
    }

    private DatabaseRoleMutationRequest CreateCustomRoleRequest(string database, DatabaseRoleMutationKind kind, string expectedDefinition) =>
        new(database, CustomRoleName, kind, CustomRolePrivileges, CustomRoleInheritedRoles, CustomRoleConfirmation, expectedDefinition);

    private string CurrentCustomRoleSignature(DatabaseRoleMutationKind kind) => BuildCustomRoleSignature(
        SelectedProfile,
        SelectedDatabase,
        CustomRoleName.Trim(),
        kind,
        CustomRolePrivileges,
        CustomRoleInheritedRoles);

    private static string BuildCustomRoleSignature(
        ConnectionProfile? profile,
        string? database,
        string roleName,
        DatabaseRoleMutationKind kind,
        string privilegesJson,
        string inheritedRolesJson) => string.Join("\u001f",
        profile?.Id.ToString("D") ?? string.Empty,
        profile?.SourceGenerationId?.ToString("D") ?? string.Empty,
        database ?? string.Empty,
        roleName,
        kind.ToString(),
        privilegesJson,
        inheritedRolesJson);

    private static string BuildDesiredCustomRoleJson(string database, string roleName, string privilegesJson, string inheritedRolesJson)
    {
        using var privileges = JsonDocument.Parse(privilegesJson);
        using var roles = JsonDocument.Parse(inheritedRolesJson);
        return JsonSerializer.Serialize(new
        {
            role = roleName,
            db = database,
            privileges = privileges.RootElement,
            roles = roles.RootElement
        }, IndentedJsonOptions);
    }

    private static string FormatRoleDiff(string? before, string? after) => JsonSerializer.Serialize(new
    {
        before = ParseJsonOrNull(before),
        after = ParseJsonOrNull(after)
    }, IndentedJsonOptions);

    private static JsonElement? ParseJsonOrNull(string? json)
    {
        if (json is null)
            return null;

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private void ClearCustomRolePreview()
    {
        _customRoleExpectedDefinition = null;
        _customRolePreviewSignature = null;
        CustomRolePreview = string.Empty;
        OnPropertyChanged(nameof(CanApplyCustomRole));
        ApplyCustomRoleCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCustomRoleTargetChanged()
    {
        ClearCustomRolePreview();
        OnPropertyChanged(nameof(CanPreviewCustomRole));
        PreviewCreateCustomRoleCommand.NotifyCanExecuteChanged();
        PreviewUpdateCustomRoleCommand.NotifyCanExecuteChanged();
        PreviewDropCustomRoleCommand.NotifyCanExecuteChanged();
    }

    partial void OnCustomRoleNameChanged(string value) => ClearCustomRolePreview();
    partial void OnCustomRolePrivilegesChanged(string value) => ClearCustomRolePreview();
    partial void OnCustomRoleInheritedRolesChanged(string value) => ClearCustomRolePreview();
}
