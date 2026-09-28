using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class EnvironmentsWindow : Window
{
    public EnvironmentsWindow() { InitializeComponent(); Opened += (_, _) => EnvironmentSelector.Focus(); }
    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        base.OnKeyDown(e);
    }
}
