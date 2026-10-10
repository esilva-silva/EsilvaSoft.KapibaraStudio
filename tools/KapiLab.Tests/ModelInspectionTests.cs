using System.Diagnostics;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ModelInspectionTests
{
    [Test]
    public async Task ValidateUsesTheIdeCatalogWithoutLoadingSyntheticWeights()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapilab-model-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "genai_config.json"),
                "{\"model\":{\"type\":\"qwen2\",\"context_length\":4096,\"decoder\":{\"filename\":\"model.onnx\"}}}");
            File.WriteAllText(Path.Combine(root, "model.onnx"), "synthetic-weight-fixture");
            File.WriteAllText(Path.Combine(root, "tokenizer_config.json"), "{}");
            File.WriteAllText(Path.Combine(root, "tokenizer.json"), JsonSerializer.Serialize(new
            {
                added_tokens = QwenFimPromptBuilder.SpecialTokens.Select((token, index) => new { content = token, id = 1000 + index })
            }));

            var catalog = new LocalModelCatalog(root, fileAccess: new KapiLabModelFileAccess());
            var validation = await catalog.ValidateAsync(root);
            var report = ModelInspection.From(validation, includeHardware: false);

            Assert.Multiple(() =>
            {
                Assert.That(report.State, Is.EqualTo(nameof(LocalModelState.Available)));
                Assert.That(report.Architecture, Is.EqualTo("Qwen2.5-Coder"));
                Assert.That(report.ModelSizeBytes, Is.GreaterThan(0));
                Assert.That(report.AvailableHardware, Is.Null);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ValidateReportsMissingFilesInsteadOfAttemptingToLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapilab-model-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new LocalModelCatalog(root, fileAccess: new KapiLabModelFileAccess());
            var validation = await catalog.ValidateAsync(root);

            Assert.That(validation.Status.State, Is.EqualTo(LocalModelState.MissingFiles));
            Assert.That(validation.Model, Is.Null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ModelFileAccessRejectsReparsePointBeforeReadingItsTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapilab-model-link-tests", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "outside");
        var link = Path.Combine(root, "package", "linked");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var sentinel = Path.Combine(target, "sentinel.txt");
        File.WriteAllText(sentinel, "external-fixture");
        try
        {
            try { CreateDirectoryLink(link, target); }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Ignore($"Esta máquina não permite criar junctions/links para a fixture local ({exception.GetType().Name}: {exception.Message}).");
            }

            Assert.Throws<UnauthorizedAccessException>(() => new KapiLabModelFileAccess().FileExists(Path.Combine(link, "sentinel.txt")));
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("external-fixture"));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        // Junctions can be created by a standard Windows account; creating a symbolic link often requires
        // Developer Mode or an elevated test host. Paths are generated locally by this fixture.
        static string QuotePowerShell(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var command = $"New-Item -ItemType Junction -Path {QuotePowerShell(link)} -Target {QuotePowerShell(target)} | Out-Null";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o criador de junction da fixture.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Falha ao criar junction da fixture: " + error);
    }

    [Test]
    public async Task ValidateRejectsDecoderPathOutsidePackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapilab-model-decoder-tests", Guid.NewGuid().ToString("N"));
        var package = Path.Combine(root, "package");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(package);
        Directory.CreateDirectory(outside);
        var externalDecoder = Path.Combine(outside, "model.onnx");
        File.WriteAllText(externalDecoder, "external-fixture");
        File.WriteAllText(Path.Combine(package, "genai_config.json"),
            "{\"model\":{\"type\":\"qwen2\",\"context_length\":4096,\"decoder\":{\"filename\":\"../outside/model.onnx\"}}}");
        try
        {
            var catalog = new LocalModelCatalog(package, fileAccess: new KapiLabModelFileAccess());
            var validation = await catalog.ValidateAsync(package);
            Assert.Multiple(() =>
            {
                Assert.That(validation.Status.State, Is.EqualTo(LocalModelState.Invalid));
                Assert.That(validation.Model, Is.Null);
                Assert.That(File.ReadAllText(externalDecoder), Is.EqualTo("external-fixture"));
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
