using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Core;
using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed class LocalizedConsoleHistoryItem(ConsoleHistoryEntry entry) : ObservableObject
{
    public ConsoleHistoryEntry Entry { get; } = entry;
    public string DisplayText => LocalizationViewModel.Current.Format("consoleHistoryItem",
        Entry.ExecutedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        ModeLabel(Entry.Mode), Entry.Connection, Entry.Database,
        Entry.Collection.Length == 0 ? "" : " › " + Entry.Collection,
        Entry.Environment, StatusLabel(Entry.Status));

    public void RefreshLanguage() => OnPropertyChanged(nameof(DisplayText));

    private static string ModeLabel(string mode) => mode switch
    {
        "Agregação" => T("modeAggregation"),
        "Script" => T("modeScript"),
        "Consulta JSON" => T("modeQuery"),
        _ => T("modeConsole")
    };

    private static string StatusLabel(string status) => status switch
    {
        "Cancelado" => T("statusCancelled"),
        "Tempo limite" => T("timedOut"),
        "Concluído" => T("statusSuccess"),
        "Erro" => T("statusError"),
        _ => status
    };

    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
}

public sealed class LocalizedConsoleResultItem(ConsoleResultSet result) : ObservableObject
{
    public ConsoleResultSet Result { get; } = result;
    public string DisplayText => LocalizationViewModel.Current.Format("consoleResultItem",
        Result.Number, Result.SourceProfile?.Name ?? "", Result.Database,
        Result.Collection is null ? "" : "." + Result.Collection,
        Result.Documents?.Count ?? 0,
        Result.IsTruncated ? LocalizationViewModel.Current.Resolve("truncatedSuffix") : "");

    public void RefreshLanguage() => OnPropertyChanged(nameof(DisplayText));
}
