using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

/// <summary>
/// Owner-modal approval. Reject is the safe default: it receives initial focus, Escape triggers it and closing the
/// window while a decision is open rejects. Enter never approves; only the explicit button does.
/// </summary>
public partial class AgentApprovalWindow : Window
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private AgentApprovalViewModel? _viewModel;

    public AgentApprovalWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Attach(DataContext as AgentApprovalViewModel);
        _clock.Tick += (_, _) => _viewModel?.Tick();
        Opened += (_, _) =>
        {
            _viewModel?.Tick();
            _clock.Start();
            RejectButton.Focus();
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _clock.Stop();
            Attach(null);
        };
    }

    private void Attach(AgentApprovalViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        if (_viewModel is { IsDecisionOpen: true } model && model.RejectCommand.CanExecute(null))
        {
            _ = model.RejectCommand.ExecuteAsync(null);
        }
        else
        {
            Close();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Closing never approves: an open decision is rejected (fire-and-forget; the runtime also denies on expiry).
        if (_viewModel is { IsDecisionOpen: true } model && model.RejectCommand.CanExecute(null))
        {
            _ = model.RejectCommand.ExecuteAsync(null);
        }
    }
}
