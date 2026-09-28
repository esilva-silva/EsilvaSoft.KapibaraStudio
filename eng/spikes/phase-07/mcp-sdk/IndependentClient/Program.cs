using System.Text.Json;

namespace EsilvaSoft.SlopStudio.Spikes.McpIndependentClient;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length is not (2 or 3))
        {
            Console.Error.WriteLine("Uso: IndependentClient <server.dll> <2025-11-25|2026-07-28> [versão do servidor]");
            return 2;
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var result = await StdioProbe.RunAsync(args[0], args[1], args.Length == 3 ? args[2] : null, deadline.Token);
            // Resumo sintético do harness; stdout do processo servidor é validado separadamente.
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("ProbeDeadlineExceeded");
            return 3;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or InvalidOperationException
            or ArgumentException or KeyNotFoundException or FormatException or System.ComponentModel.Win32Exception)
        {
            // Não refletir paths, environment ou payload remoto em stderr.
            Console.Error.WriteLine("ProbeRejected");
            return 4;
        }
    }
}
