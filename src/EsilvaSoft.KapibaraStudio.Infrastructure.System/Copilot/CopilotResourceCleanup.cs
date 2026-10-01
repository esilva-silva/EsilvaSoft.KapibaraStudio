namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

internal static class CopilotResourceCleanup
{
    public static async Task DisposeAllAsync(IEnumerable<IAsyncDisposable> resources)
    {
        List<Exception> failures = [];
        foreach (var resource in resources)
        {
            try { await resource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }

        if (failures.Count > 0)
            throw new AggregateException("Falha ao descartar recursos do runtime Copilot.", failures).Flatten();
    }
}
