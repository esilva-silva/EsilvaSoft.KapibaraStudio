namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Converts an OS console interruption into cooperative command cancellation.</summary>
internal sealed class ConsoleCommandCancellation : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    public ConsoleCommandCancellation() => Console.CancelKeyPress += OnCancelKeyPress;

    public CancellationToken Token => _cancellation.Token;
    public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        _cancellation.Cancel();
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _cancellation.Dispose();
    }
}
