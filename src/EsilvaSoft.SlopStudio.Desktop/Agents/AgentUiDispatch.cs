using Avalonia.Threading;

namespace EsilvaSoft.SlopStudio.Desktop.Agents;

/// <summary>
/// Marshalling of the Desktop agent ports, which are called by the registry/broker on worker threads, to the Avalonia
/// UI thread that owns every view model. Synchronous reads use a short deadline and a fail-closed fallback instead of
/// blocking the caller forever; nothing here is ever awaited while holding a lock.
/// </summary>
internal static class AgentUiDispatch
{
    /// <summary>Default deadline of a synchronous read of UI state from a worker thread.</summary>
    public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs <paramref name="read"/> on the UI thread and returns its value, or <paramref name="fallback"/> when the UI
    /// thread does not answer within <paramref name="timeout"/> or the read throws. Called on the UI thread it runs inline.
    /// </summary>
    public static T ReadOnUi<T>(Func<T> read, T fallback, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                return read();
            }

            var operation = Dispatcher.UIThread.InvokeAsync(read, DispatcherPriority.Send);
            var task = operation.GetTask();
            return task.Wait(timeout ?? DefaultReadTimeout) ? task.Result : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>Posts <paramref name="action"/> to the UI thread (inline when already on it).</summary>
    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    /// <summary>Runs an asynchronous UI operation from any thread and returns its task.</summary>
    public static Task<T> RunOnUiAsync<T>(Func<Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Dispatcher.UIThread.CheckAccess() ? body() : Dispatcher.UIThread.InvokeAsync(body);
    }
}
