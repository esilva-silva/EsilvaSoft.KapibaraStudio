using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<AuditEntry> AuditEntries { get; } = [];

    [ObservableProperty]
    private string _auditExportPath = string.Empty;

    [RelayCommand]
    private async Task LoadAuditAsync()
    {
        await RunAsync(async cancellationToken =>
        {
            var entries = await _workspace.GetRecentAuditAsync(cancellationToken: cancellationToken);
            AuditEntries.Clear();
            foreach (var entry in entries)
            {
                AuditEntries.Add(entry);
            }

            AdministrationResults = entries.Count == 0
                ? T("auditInitialNone")
                : string.Join(Environment.NewLine, entries.Select(entry => entry.DisplayText));
            StatusMessage = F("auditLoaded", entries.Count);
        });
    }

    [RelayCommand]
    private async Task ExportAuditAsync()
    {
        if (string.IsNullOrWhiteSpace(AuditExportPath))
        {
            SetError(T("auditPathRequired"));
            return;
        }

        if (!string.Equals(Path.GetExtension(AuditExportPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            SetError(T("auditExtension"));
            return;
        }

        try
        {
            var entries = await _workspace.GetRecentAuditAsync(500);
            var json = AuditJsonSerializer.Serialize(entries);
            await _workspace.WriteNewTextExportAsync(AuditExportPath, json);
            StatusMessage = F("auditExported", AuditExportPath);
        }
        catch (TextExportFileAlreadyExistsException)
        {
            SetError(T("auditAlreadyExists"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetError(F("auditExportFailed", exception.Message));
        }
    }
}
