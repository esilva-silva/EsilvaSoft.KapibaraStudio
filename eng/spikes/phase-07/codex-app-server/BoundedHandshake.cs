using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Spike-only transport. Never returns subprocess text, paths, or diagnostic content.
public static class SlopCodexHandshake
{
    public sealed class Evidence
    {
        public string PlatformFamily { get; set; }
        public string PlatformOs { get; set; }
        public bool CodexHomeMatchesScratch { get; set; }
        public int ResponseBytes { get; set; }
        public int TrailingStdoutBytes { get; set; }
        public int StderrBytes { get; set; }
        public int ExitCode { get; set; }
    }

    private const int MaxFrameBytes = 16384;
    private const int MaxDrainBytes = 65536;

    public sealed class Manifest
    {
        public string Sha256 { get; set; }
        public string Version { get; set; }
    }

    public static Manifest ReadSchemaManifest(string path)
    {
        const int maxBytes = 1024 * 1024;
        try
        {
            if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException();
            var fullPath = Path.GetFullPath(path);
            // Reject links in the leaf and all existing ancestors before opening.
            // This is not atomic protection against another writer replacing a path.
            for (var current = fullPath; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException();
            }
            if ((File.GetAttributes(fullPath) & (FileAttributes.Directory | FileAttributes.Device)) != 0)
                throw new InvalidOperationException();
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length == 0 || stream.Length > maxBytes) throw new InvalidOperationException();
            // Bound reads too: a pre-read length check alone cannot bound a growing file.
            var bytes = new byte[maxBytes + 1];
            var count = 0;
            int read;
            while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) > 0)
                count += read;
            if (count == 0 || count > maxBytes) throw new InvalidOperationException();
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name)) throw new InvalidOperationException();
            var scope = root.GetProperty("scope").GetString();
            var hash = root.GetProperty("executableSha256").GetString();
            var version = root.GetProperty("version").GetString();
            if (scope != "availability-and-version-specific-schema-only" || hash == null || hash.Length != 64 ||
                !Regex.IsMatch(hash, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant) ||
                version == null || version.Length > 128 ||
                !Regex.IsMatch(version, "\\Acodex-cli [0-9]+\\.[0-9]+\\.[0-9]+[a-zA-Z0-9.+-]*\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException();
            return new Manifest { Sha256 = hash, Version = version };
        }
        catch
        {
            // JSON parse errors can quote an untrusted token or path. Never echo them.
            throw new InvalidOperationException("Manifesto recusado: arquivo ausente, link, tamanho, JSON ou metadados inválidos (máximo 1 MiB e profundidade 8).");
        }
    }

    private static async Task<int> DrainAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[1024];
        var total = 0;
        int count;
        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
        {
            total += count;
            if (total > MaxDrainBytes)
                throw new InvalidOperationException("Limite de saída excedido; conteúdo descartado.");
        }
        return total;
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        // One bounded frame, no unbounded ReadLine/ReadToEnd allocation.
        using var frame = new MemoryStream();
        var one = new byte[1];
        while (await stream.ReadAsync(one, 0, 1, token) != 0)
        {
            if (one[0] == 10) return frame.ToArray();
            if (frame.Length == MaxFrameBytes)
                throw new InvalidOperationException("Frame de inicialização excede 16 KiB.");
            frame.WriteByte(one[0]);
        }
        throw new InvalidOperationException("EOF antes do frame de inicialização.");
    }

    private static async Task WriteAsync(Stream stream, string message, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(message + "\n");
        await stream.WriteAsync(bytes, 0, bytes.Length, token);
        await stream.FlushAsync(token);
    }

    public static async Task<Evidence> RunAsync(ProcessStartInfo info, string scratchHome, int timeoutSeconds)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = info };
        Task<int> stderr = null;
        Task<int> trailing = null;
        var started = false;
        try
        {
            started = process.Start();
            if (!started) throw new InvalidOperationException("Processo não iniciou.");
            stderr = DrainAsync(process.StandardError.BaseStream, deadline.Token);
            // A failed drain cancels the handshake immediately rather than blocking on a full pipe.
            _ = stderr.ContinueWith(t => deadline.Cancel(), CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            await WriteAsync(process.StandardInput.BaseStream,
                "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"slop_phase07_handshake_probe\",\"title\":\"EsilvaSoft.SlopStudio handshake spike\",\"version\":\"0.1.0\"}}}", deadline.Token);
            var frame = await ReadFrameAsync(process.StandardOutput.BaseStream, deadline.Token);
            Evidence evidence;
            using (var document = JsonDocument.Parse(frame, new JsonDocumentOptions { MaxDepth = 16 }))
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || id.GetInt32() != 1 ||
                    root.TryGetProperty("error", out _) || root.TryGetProperty("method", out _) ||
                    !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Resposta inesperada; conteúdo descartado.");
                // Match the required fields in the pinned version's InitializeResponse schema.
                var home = result.GetProperty("codexHome").GetString();
                var userAgent = result.GetProperty("userAgent").GetString();
                var family = result.GetProperty("platformFamily").GetString();
                var os = result.GetProperty("platformOs").GetString();
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (string.IsNullOrEmpty(userAgent) || !Path.IsPathFullyQualified(home) ||
                    !string.Equals(Path.GetFullPath(home), Path.GetFullPath(scratchHome), comparison) ||
                    (family != "windows" && family != "unix") ||
                    (os != "windows" && os != "linux" && os != "macos"))
                    throw new InvalidOperationException("Metadados incompatíveis; conteúdo descartado.");
                evidence = new Evidence { PlatformFamily = family, PlatformOs = os,
                    CodexHomeMatchesScratch = true, ResponseBytes = frame.Length };
            }
            Array.Clear(frame, 0, frame.Length);
            await WriteAsync(process.StandardInput.BaseStream, "{\"method\":\"initialized\",\"params\":{}}", deadline.Token);
            process.StandardInput.Close();
            trailing = DrainAsync(process.StandardOutput.BaseStream, deadline.Token);
            await Task.WhenAll(trailing, stderr).WaitAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            evidence.TrailingStdoutBytes = await trailing;
            evidence.StderrBytes = await stderr;
            evidence.ExitCode = process.ExitCode;
            if (evidence.ExitCode != 0)
                throw new InvalidOperationException("Processo não encerrou normalmente após EOF.");
            return evidence;
        }
        catch
        {
            // Exceptions from JSON/I/O can contain subprocess content. Do not expose inner errors.
            throw new InvalidOperationException("Handshake recusado: timeout, saída inválida/excessiva ou falha de processo; nenhum conteúdo bruto foi registrado.");
        }
        finally
        {
            deadline.Cancel();
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                    throw new InvalidOperationException("Não foi possível confirmar encerramento do processo de probe.");
            }
            // Observe canceled/faulted reader tasks without keeping a live process or unbounded wait.
            if (stderr != null) { try { await stderr.WaitAsync(TimeSpan.FromSeconds(1)); } catch { } }
            if (trailing != null) { try { await trailing.WaitAsync(TimeSpan.FromSeconds(1)); } catch { } }
        }
    }
}
