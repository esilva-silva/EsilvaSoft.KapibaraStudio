using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// Static guard over the WinML lockfiles. The managed ONNX Runtime asks the native runtime for its own API version;
/// WinML loads the native ORT shipped by <c>Microsoft.Windows.AI.MachineLearning</c>, not the one GenAI ships for
/// Cpu/Cuda. Managed 1.31.0 over WinML 2.1.1 (native ORT 1.24.6, APIs 1–24) failed with "requested API version [31]
/// is not available" and no model could initialize; 1.28.0 and 1.30.0 negotiate down and were verified on real
/// hardware. Runs without loading native code, so it holds in CI where real runtimes and models are excluded.
/// </summary>
[TestFixture, Category("Integration")]
public sealed class OnnxRuntimeManagedVersionGuardTests
{
    // WinML runtime version → highest managed ORT version verified to initialize against its native ORT.
    private static readonly Dictionary<string, Version> VerifiedManagedForWinMl = new(StringComparer.Ordinal)
    {
        ["2.1.1"] = new(1, 30, 0)
    };

    private static readonly string[] WinMlLockFiles =
    [
        "src/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter/packages.WinML.lock.json",
        "src/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter/packages.WinML.win-arm64.lock.json",
        "src/EsilvaSoft.KapibaraStudio.Desktop/packages.WinML.lock.json",
        "src/EsilvaSoft.KapibaraStudio.Desktop/packages.WinML.win-arm64.lock.json"
    ];

    [TestCaseSource(nameof(WinMlLockFiles))]
    public void WinMlManagedOnnxRuntimeNeverOutrunsTheNativeRuntimeOfWindowsMl(string lockFile)
    {
        var path = Path.Combine(FindRepositoryRoot(), lockFile);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var winMl = Resolved(document, "Microsoft.Windows.AI.MachineLearning");
        var managed = Resolved(document, "Microsoft.ML.OnnxRuntime.Managed");
        Assert.That(winMl, Is.Not.Null, $"{lockFile} não resolve Microsoft.Windows.AI.MachineLearning.");
        Assert.That(managed, Is.Not.Null, $"{lockFile} não resolve Microsoft.ML.OnnxRuntime.Managed.");
        Assert.That(VerifiedManagedForWinMl.TryGetValue(winMl!, out var verified) && verified is not null, Is.True,
            $"WinML {winMl} sem versão gerenciada verificada: valide OrtEnv.Instance() com o onnxruntime.dll desse runtime e registre aqui.");
        Assert.That(Version.Parse(managed!), Is.LessThanOrEqualTo(verified!),
            $"Microsoft.ML.OnnxRuntime.Managed {managed} pede uma API mais nova do que o ORT nativo do WinML {winMl} oferece; nenhum modelo inicializaria.");
    }

    private static string? Resolved(JsonDocument document, string package)
    {
        foreach (var target in document.RootElement.GetProperty("dependencies").EnumerateObject())
            if (target.Value.TryGetProperty(package, out var entry) && entry.TryGetProperty("resolved", out var resolved))
                return resolved.GetString();
        return null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EsilvaSoft.KapibaraStudio.slnx")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null, "Raiz do repositório não encontrada.");
        return directory!.FullName;
    }
}
