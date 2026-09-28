using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class AgentPermissionsWindow : Window
{
    public AgentPermissionsWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            if (DataContext is AgentPermissionsViewModel vm) await vm.LoadTask;
            Focus();
        };
    }

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
