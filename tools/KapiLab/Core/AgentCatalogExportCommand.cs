using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class AgentCatalogExportCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<int> RunAsync(string providerId, string surface, string? output, string? workspace)
    {
        var root = LabWorkspace.Resolve(workspace) ?? throw new ArgumentException("Informe --workspace ou KAPILAB_WORKSPACE.");
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (surface is not ("in-process" or "session-channel")) throw new InvalidDataException("Superfície de catálogo inválida.");

        var outputPath = output is null ? null : LabWorkspace.ResolveOutput(root, output);
        var tempRoot = Path.Combine(Path.GetTempPath(), "kapilab-agent-catalog-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        AgentToolCatalogSnapshot.Snapshot snapshot;
        try
        {
            var services = new ServiceCollection();
            services.AddKapibaraStudioInfrastructure(Path.Combine(tempRoot, "workspace.db"),
                new AgentPlatformOptions { InProcessToolExposureStage = AgentToolExposureStage.Metadata });
            await using var provider = services.BuildServiceProvider();
            snapshot = AgentToolCatalogSnapshot.Export(provider.GetRequiredService<IAgentToolRegistry>(), providerId, surface);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { }
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        if (outputPath is null) Console.Out.WriteLine(json);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, json, new System.Text.UTF8Encoding(false)).ConfigureAwait(false);
                File.Move(temporary, outputPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return 0;
    }
}
