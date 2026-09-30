using System.Text;
using EsilvaSoft.KapibaraStudio.Application.Agents;
namespace EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
internal static class ClaudeCodeProbe
{
    public static async Task<ClaudeCodeProbeResult> RunAsync(
        string executable, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, int maxOutputBytes,
        int maxStderrBytes, CancellationToken cancellationToken)
    {
        await using var process = ClaudeCodeProcess.Start(executable, arguments, workingDirectory, maxStderrBytes);
        // Sem entrada: o stdin fechado impede qualquer espera interativa.
        process.StandardInput.Close();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var buffer = new MemoryStream();
        var overflow = false;
        try
        {
            var chunk = new byte[4096];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0)
            {
                var room = maxOutputBytes - (int)buffer.Length;
                if (read > room)
                {
                    overflow = true;
                    process.KillTree();
                    break;
                }

                buffer.Write(chunk, 0, read);
            }

            if (!overflow && !await process.WaitForExitAsync(RemainingOrMinimum(deadline)).ConfigureAwait(false))
            {
                process.KillTree();
                return new ClaudeCodeProbeResult(true, false, null, string.Empty);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.KillTree();
            return new ClaudeCodeProbeResult(true, false, null, string.Empty);
        }
        catch (OperationCanceledException)
        {
            process.KillTree();
            throw;
        }

        var output = overflow ? string.Empty : Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        return new ClaudeCodeProbeResult(false, overflow, overflow ? null : process.ExitCode, output);
    }

    private static TimeSpan RemainingOrMinimum(CancellationTokenSource deadline) =>
        deadline.IsCancellationRequested ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromSeconds(2);
}
