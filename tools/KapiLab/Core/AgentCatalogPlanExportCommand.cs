using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class AgentCatalogPlanExportCommand
{
    internal static async Task<int> RunAsync(string providerId, string input, string? output, string? workspace)
    {
        var root = LabWorkspace.Resolve(workspace) ?? throw new ArgumentException("Informe --workspace ou KAPILAB_WORKSPACE.");
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var inputPath = KapiLabCommandLine.ReadArtifactPath(input, root);
        var planInput = AgentToolCatalogPlanSnapshot.ParseInput(LabWorkspace.ReadUtf8FileLimited(inputPath, 1024 * 1024));
        if (planInput is null || !string.Equals(planInput.Case.Permissions.ProviderId, providerId, StringComparison.Ordinal))
            throw new InvalidDataException("Plano sintético inválido ou provider divergente.");

        var outputPath = output is null ? null : LabWorkspace.ResolveOutput(root, output);
        if (outputPath is not null) LabWorkspace.RefuseInputOverwrite(root, outputPath, [inputPath]);
        var tempRoot = Path.Combine(Path.GetTempPath(), "kapilab-agent-catalog-plan-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        AgentToolCatalogPlanSnapshot.Snapshot snapshot;
        try
        {
            var services = new ServiceCollection();
            services.AddKapibaraStudioInfrastructure(Path.Combine(tempRoot, "workspace.db"),
                new AgentPlatformOptions { InProcessToolExposureStage = AgentToolExposureStage.Metadata });
            await using var serviceProvider = services.BuildServiceProvider();
            snapshot = AgentToolCatalogPlanSnapshot.Export(serviceProvider.GetRequiredService<IAgentToolRegistry>(), providerId, planInput);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { }
        }

        var json = AgentToolCatalogPlanSnapshot.Serialize(snapshot);
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
