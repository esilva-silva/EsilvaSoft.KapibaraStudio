using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class AgentEditProposalReviewWindow : Window
{
    public AgentEditProposalReviewWindow() => InitializeComponent();

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
