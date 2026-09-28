using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>
/// Confirmation of the global CLI sign-out. "Cancel" has the initial focus and is the safe action: Escape, Enter on
/// the focused Cancel button and closing the window all keep the session; only an explicit click/activation of the
/// confirm button returns true.
/// </summary>
public partial class AgentCliSignOutWindow : Window
{
    public AgentCliSignOutWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(false);
            }
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => CancelButton.Focus(NavigationMethod.Tab);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>True only after the confirm button was activated.</summary>
    public bool Confirmed { get; private set; }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }
}
