using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

/// <summary>Only an explicitly configured local binary, an isolated dbpath and loopback binding are used.</summary>
internal sealed class DisposableCopilotMongoServer(Process process, string uri) : IDisposable
{
    public string Uri { get; } = uri;

    public static async Task<DisposableCopilotMongoServer> StartAsync(string root)
    {
        var executable = Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGOD");
        if (string.IsNullOrWhiteSpace(executable))
            Assert.Ignore("Defina SLOP_CONSOLE_MONGOD para o mongod local descartável; MongoDB real continua pendente.");
        Assert.That(Path.IsPathFullyQualified(executable!), Is.True,
            "O mongod precisa ser configurado por caminho absoluto.");
        Assert.That(File.Exists(executable), Is.True, "O binário mongod configurado não existe.");
        if (OperatingSystem.IsLinux())
            Assert.That(File.GetUnixFileMode(executable!) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute),
                Is.Not.EqualTo((UnixFileMode)0), "O mongod precisa de permissão de execução Unix.");

        var dbpath = Path.Combine(root, "mongo-data");
        Directory.CreateDirectory(dbpath);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo(executable!)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[] { "--dbpath", dbpath, "--port", port.ToString(CultureInfo.InvariantCulture),
            "--bind_ip", "127.0.0.1", "--logpath", Path.Combine(root, "mongod.log"), "--wiredTigerCacheSizeGB", "0.25" })
            start.ArgumentList.Add(argument);
        var spawned = Process.Start(start) ?? throw new InvalidOperationException("O mongod descartável não iniciou.");
        var uri = $"mongodb://127.0.0.1:{port}/?directConnection=true&serverSelectionTimeoutMS=300";
        var server = new DisposableCopilotMongoServer(spawned, uri);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var client = new MongoClient(uri);
            while (true)
            {
                if (spawned.HasExited) throw new InvalidOperationException("O mongod descartável encerrou durante startup.");
                timeout.Token.ThrowIfCancellationRequested();
                try
                {
                    await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1),
                        cancellationToken: timeout.Token);
                    return server;
                }
                catch (TimeoutException) { await Task.Delay(100, timeout.Token); }
            }
        }
        catch { server.Dispose(); throw; }
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(10_000)) throw new IOException("O mongod descartável não encerrou para limpeza.");
        }
        finally { process.Dispose(); }
    }
}
