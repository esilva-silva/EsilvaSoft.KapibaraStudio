using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class EnvironmentsViewModel : ObservableObject
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    private readonly WorkspaceService _workspace;
    private bool _loaded;
    public ObservableCollection<EnvironmentDefinition> Environments { get; } = [];
    public ObservableCollection<string> Keys { get; } = [];
    [ObservableProperty] private EnvironmentDefinition? _selectedEnvironment;
    [ObservableProperty] private string? _selectedKey;
    [ObservableProperty] private string _environmentName = "";
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _status = "";
    public event EventHandler? Saved;

    public EnvironmentsViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
        try
        {
            var vault = workspace.LoadEnvironments();
            foreach (var environment in vault.Environments) Environments.Add(environment);
            SelectedEnvironment = Environments.Single(e => e.Name == vault.ActiveEnvironment);
            Status = F("environmentActive", vault.ActiveEnvironment);
            _loaded = true;
        }
        catch (Exception ex) { Status = F("loadEnvironmentsFailed", ex.Message); }
    }

    partial void OnSelectedEnvironmentChanged(EnvironmentDefinition? value)
    {
        Keys.Clear();
        if (value is not null) foreach (var key in value.Values.Keys.Order()) Keys.Add(key);
        Key = ""; Value = ""; SelectedKey = null;
    }
    partial void OnSelectedKeyChanged(string? value)
    {
        Key = value ?? "";
        Value = value is not null && SelectedEnvironment is not null ? SelectedEnvironment.Values[value] : "";
    }

    [RelayCommand]
    private void AddEnvironment()
    {
        var name = EnvironmentName.Trim();
        if (!_loaded || name.Length is 0 or > 60 || Environments.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        { Status = T("environmentNameRequired"); return; }
        var environment = new EnvironmentDefinition(name, []);
        Environments.Add(environment); SelectedEnvironment = environment; EnvironmentName = "";
        Status = T("environmentAdded");
    }

    [RelayCommand]
    private void SetValue()
    {
        if (SelectedEnvironment is null || string.IsNullOrWhiteSpace(Key)) { Status = T("environmentAndKeyRequired"); return; }
        var key = Key.Trim();
        SelectedEnvironment.Values[key] = Value;
        if (!Keys.Contains(key)) Keys.Add(key);
        Key = ""; Value = ""; SelectedKey = null;
        Status = T("environmentVariableUpdated");
    }

    [RelayCommand]
    private void RemoveValue()
    {
        if (SelectedEnvironment is null || SelectedKey is null) return;
        var key = SelectedKey;
        SelectedEnvironment.Values.Remove(key); Keys.Remove(key);
        Key = ""; Value = ""; SelectedKey = null;
        Status = T("environmentVariableRemoved");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!_loaded || SelectedEnvironment is null) return;
        if (!string.IsNullOrEmpty(Key) || !string.IsNullOrEmpty(Value))
        { Status = T("environmentPendingVariable"); return; }
        try
        {
            var selectedName = SelectedEnvironment.Name;
            var snapshot = new EnvironmentVault(1, selectedName, Environments.Select(environment => new EnvironmentDefinition(environment.Name, new(environment.Values))).ToArray());
            await _workspace.SaveEnvironmentsAsync(snapshot);
            Status = F("environmentReopenConnections", selectedName);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Status = F("environmentsNotSaved", ex.Message); }
    }
}
