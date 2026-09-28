using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>
/// Provider settings. Opening it lists capabilities only; it never starts authentication or runs a CLI. The global
/// sign-out confirmation and "Copy command" are view concerns; every account operation lives in the ViewModel.
/// </summary>
public partial class AgentSettingsWindow : Window
{
    public event EventHandler? PermissionsRequested;
    public AgentSettingsWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            if (ProviderList.IsVisible)
            {
                ProviderList.Focus();
            }
            else
            {
                CloseButton.Focus();
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AgentSettingsViewModel model)
            {
                model.ConfirmSignOut ??= ConfirmSignOutAsync;
            }
        };
        // The typed key never outlives the window; closing also ends any wait for the CLI window (which stays open).
        Closed += (_, _) =>
        {
            if (DataContext is AgentSettingsViewModel model)
            {
                model.ApiKey = "";
                model.Dispose();
            }
        };
    }

    /// <summary>The confirmation window currently shown (for tests and focus return).</summary>
    public AgentCliSignOutWindow? OpenSignOutConfirmation { get; private set; }

    private async Task<bool> ConfirmSignOutAsync(AgentCliSignOutPrompt prompt)
    {
        var dialog = new AgentCliSignOutWindow { DataContext = prompt };
        OpenSignOutConfirmation = dialog;
        try
        {
            if (IsVisible)
            {
                return await dialog.ShowDialog<bool>(this) && dialog.Confirmed;
            }

            var closed = new TaskCompletionSource<bool>();
            dialog.Closed += (_, _) => closed.TrySetResult(dialog.Confirmed);
            dialog.Show();
            return await closed.Task;
        }
        finally
        {
            OpenSignOutConfirmation = null;
            // After the command settles (it is still running while the dialog is open), focus returns to Sign out when
            // it is still available, otherwise to Test connection.
            Dispatcher.UIThread.Post(() => (SignOutButton.IsEffectivelyEnabled ? SignOutButton : TestConnectionButton).Focus(),
                DispatcherPriority.Background);
        }
    }

    private async void CopyManualCommand(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is AgentSettingsViewModel { ManualCommand: { Length: > 0 } command } && Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(command);
            }
        }
        catch (Exception)
        {
            // The command stays visible and selectable in the read-only box; copying is a convenience.
        }
    }

    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();

    private void OpenPermissions(object? sender, RoutedEventArgs e) => PermissionsRequested?.Invoke(this, EventArgs.Empty);
}
