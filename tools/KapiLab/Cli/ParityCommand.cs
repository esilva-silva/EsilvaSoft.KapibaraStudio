using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class ParityCommand
{
    private const int MaximumArtifactBytes = 16 * 1024 * 1024;
    private const int MaximumValues = 1_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static int Run(string input, string? tolerancesPath, string? output, string? workspaceOption)
    {
        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null) return Usage("informe --workspace ou KAPILAB_WORKSPACE.");

        try
        {
            var inputPath = ResolveInput(input, workspace);
            var inputPaths = new List<string> { inputPath };
            var inputInfo = new FileInfo(inputPath);
            if (inputInfo.Length > MaximumArtifactBytes) throw new InvalidDataException("Paridade excede o limite de leitura.");
            var inputBytes = LabWorkspace.ReadFileLimited(inputPath, MaximumArtifactBytes);
            _ = new UTF8Encoding(false, true).GetString(inputBytes);
            JsonContractValidation.RequireUniqueProperties(inputBytes, maximumDepth: 64);
            var pair = JsonSerializer.Deserialize<ParityObservationPair>(inputBytes, JsonOptions)
                ?? throw new InvalidDataException("Observações de paridade inválidas.");
            Validate(pair);

            ParityTolerances? tolerances = null;
            string? toleranceHash = null;
            if (tolerancesPath is not null)
            {
                var toleranceFile = ResolveInput(tolerancesPath, workspace);
                inputPaths.Add(toleranceFile);
                if (new FileInfo(toleranceFile).Length > 64 * 1024) throw new InvalidDataException("Tolerâncias excedem o limite.");
                var toleranceBytes = LabWorkspace.ReadFileLimited(toleranceFile, 64 * 1024);
                _ = new UTF8Encoding(false, true).GetString(toleranceBytes);
                JsonContractValidation.RequireUniqueProperties(toleranceBytes, maximumDepth: 32);
                tolerances = JsonSerializer.Deserialize<ParityTolerances>(toleranceBytes, JsonOptions)
                    ?? throw new InvalidDataException("Arquivo de tolerâncias inválido.");
                toleranceHash = Convert.ToHexStringLower(SHA256.HashData(toleranceBytes));
            }

            var result = Compare(pair, tolerances);
            var report = new ParityReport("kapilab-parity-report-v1", pair.Level, result.Status,
                ObservationCount(pair.Level, pair.Expected), ObservationCount(pair.Level, pair.Actual), result.DifferenceCount, result.Observed, result.Tolerance,
                result.Path, tolerances?.Schema, tolerances?.Version,
                Convert.ToHexStringLower(SHA256.HashData(inputBytes)), toleranceHash);
            var serialized = JsonSerializer.Serialize(report, JsonOptions);
            if (string.IsNullOrWhiteSpace(output) || output == "-") Console.Out.WriteLine(serialized);
            else
            {
                var destination = LabWorkspace.ResolveOutput(workspace, output);
                LabWorkspace.RefuseInputOverwrite(workspace, destination, inputPaths);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, serialized, new System.Text.UTF8Encoding(false));
                    File.Move(temporary, destination, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return result.Status switch
            {
                "matched" => (int)ExitCode.Success,
                "mismatched" => (int)ExitCode.ParityMismatch,
                _ => (int)ExitCode.GateFailed,
            };
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("error entrada de paridade recusada por política de privacidade/caminho.");
            return (int)ExitCode.PrivacyViolation;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException
            or ArgumentException or FormatException or OverflowException)
        {
            Console.Error.WriteLine("error observações de paridade/tolerâncias inválidas ou inacessíveis.");
            return (int)ExitCode.InvalidInput;
        }
    }

    private static ParityResult Compare(ParityObservationPair pair, ParityTolerances? tolerances) => pair.Level switch
    {
        "tokenizer" => ParityComparison.CompareTokenizer(pair.Expected.TokenIds!, pair.Actual.TokenIds!),
        "prompt" => ParityComparison.ComparePrompt(
            Convert.FromBase64String(pair.Expected.PromptUtf8Base64!), Convert.FromBase64String(pair.Actual.PromptUtf8Base64!)),
        "greedy" => ParityComparison.CompareGreedy(pair.Expected.TokenIds!, pair.Actual.TokenIds!, RequireTolerances(tolerances)),
        "teacher" => ParityComparison.CompareTeacherForcing(pair.Expected.Logits!, pair.Actual.Logits!, RequireTolerances(tolerances)),
        _ => throw new InvalidDataException("Nível de paridade desconhecido."),
    };

    private static ParityTolerances RequireTolerances(ParityTolerances? tolerances) => tolerances
        ?? throw new InvalidDataException("Greedy e teacher exigem arquivo de tolerâncias versionado.");

    private static int ObservationCount(string level, ParityObservation observation) => level switch
    {
        "tokenizer" or "greedy" => observation.TokenIds!.Count,
        "prompt" => Convert.FromBase64String(observation.PromptUtf8Base64!).Length,
        "teacher" => observation.Logits!.Count,
        _ => throw new InvalidDataException("Nível de paridade desconhecido."),
    };

    private static void Validate(ParityObservationPair pair)
    {
        if (pair.Schema != "kapilab-parity-observations-v1" || pair.Level is not ("tokenizer" or "prompt" or "greedy" or "teacher")
            || pair.Expected is null || pair.Actual is null || pair.Expected.Schema != "kapilab-parity-observation-v1"
            || pair.Actual.Schema != "kapilab-parity-observation-v1")
            throw new InvalidDataException("Schema ou nível de observação inválido.");

        switch (pair.Level)
        {
            case "tokenizer":
            case "greedy":
                if (pair.Expected.TokenIds is null || pair.Actual.TokenIds is null
                    || pair.Expected.TokenIds.Count > MaximumValues || pair.Actual.TokenIds.Count > MaximumValues
                    || pair.Expected.TokenIds.Any(value => value < 0) || pair.Actual.TokenIds.Any(value => value < 0))
                    throw new InvalidDataException("IDs de token ausentes, negativos ou acima do limite.");
                break;
            case "prompt":
                if (pair.Expected.PromptUtf8Base64 is null || pair.Actual.PromptUtf8Base64 is null)
                    throw new InvalidDataException("Bytes UTF-8 base64 ausentes.");
                var expectedPrompt = Convert.FromBase64String(pair.Expected.PromptUtf8Base64);
                var actualPrompt = Convert.FromBase64String(pair.Actual.PromptUtf8Base64);
                if (expectedPrompt.Length > MaximumValues || actualPrompt.Length > MaximumValues)
                    throw new InvalidDataException("Prompt excede o limite de comparação.");
                _ = new UTF8Encoding(false, true).GetString(expectedPrompt);
                _ = new UTF8Encoding(false, true).GetString(actualPrompt);
                break;
            case "teacher":
                if (pair.Expected.Logits is null || pair.Actual.Logits is null
                    || pair.Expected.Logits.Count > MaximumValues || pair.Actual.Logits.Count > MaximumValues)
                    throw new InvalidDataException("Logits ausentes ou acima do limite.");
                break;
        }
    }

    private static string ResolveInput(string path, string workspace)
    {
        var fullPath = Path.GetFullPath(path, workspace);
        var relative = Path.GetRelativePath(workspace, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Entradas devem estar dentro do workspace.");
        LabWorkspace.RefuseBlindInput(fullPath, workspace);
        return fullPath;
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine("error " + message);
        return (int)ExitCode.Usage;
    }

    private sealed record ParityObservationPair(string Schema, string Level, ParityObservation Expected, ParityObservation Actual);
    private sealed record ParityObservation(string Schema, IReadOnlyList<int>? TokenIds = null,
        string? PromptUtf8Base64 = null, IReadOnlyList<double>? Logits = null);
    private sealed record ParityReport(string Schema, string Level, string Status, int ExpectedCount, int ActualCount,
        int? DifferenceCount, double? Observed, double? Tolerance, string? Path, string? ToleranceSchema,
        string? ToleranceVersion, string ObservationsSha256, string? ToleranceSha256);
}
