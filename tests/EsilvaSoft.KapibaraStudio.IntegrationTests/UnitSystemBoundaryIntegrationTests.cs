using System.Text.RegularExpressions;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// Static architecture guard for test projects intended to remain isolated. It runs in IntegrationTests because
/// discovering repository source files is itself a filesystem operation.
/// </summary>
[TestFixture, Category("Integration")]
public sealed class UnitSystemBoundaryIntegrationTests
{
    private static readonly Dictionary<string, Regex> ForbiddenCalls = new(StringComparer.Ordinal)
    {
        ["filesystem"] = new(@"\b(?:File|Directory)\.(?:Read\w*|Write\w*|Delete|Move|Copy|Exists|Open\w*|Create\w*|Get\w*|Enumerate\w*|Set\w*)\s*\(|\bnew\s+(?:global::)?(?:\w+\.)*(?:FileStream|FileInfo|DirectoryInfo)\s*\(", RegexOptions.CultureInvariant),
        ["process"] = new(@"\bProcess\.Start\s*\(|\bnew\s+(?:global::)?(?:\w+\.)*(?:Process|ProcessStartInfo)\s*\(", RegexOptions.CultureInvariant),
        ["console"] = new(@"\bConsole\.(?:OpenStandard\w*|Read\w*|Write\w*|Set\w*)\s*\(", RegexOptions.CultureInvariant),
        ["host"] = new(@"\bEnvironment\.(?:GetEnvironmentVariable|SetEnvironmentVariable|GetFolderPath)\b|\bEnvironment\.(?:CurrentDirectory|SystemDirectory|MachineName|UserName)\b|\bAppContext\.BaseDirectory\b|\bPath\.GetTemp(?:Path|FileName)\s*\(|\bPath\.GetFullPath\s*\((?:[^(),]|\([^()]*\))*\)", RegexOptions.CultureInvariant),
        ["native security"] = new(@"\b(?:ProtectedData|WindowsIdentity)\.|\.(?:GetAccessControl|SetAccessControl|GetUnixFileMode|SetUnixFileMode)\s*\(|\[(?:DllImport|LibraryImport)\(", RegexOptions.CultureInvariant),
        ["real runtime"] = new(@"\bnew\s+(?:global::)?(?:\w+\.)*(?:LiteDatabase|MongoClient|MongoClientPool|CopilotClient|Socket|NamedPipeServerStream|NamedPipeClientStream|TcpListener)\s*\(|\bnew\s+HttpClient\s*\(\s*\)", RegexOptions.CultureInvariant),
    };

    [Test]
    public void UnitAndTestSupportSourcesDoNotAccessHostResourcesDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var unitTestRoots = new[]
        {
            Path.Combine(repositoryRoot, "tests", "EsilvaSoft.KapibaraStudio.UnitTests"),
            Path.Combine(repositoryRoot, "tests", "EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests"),
            Path.Combine(repositoryRoot, "tests", "EsilvaSoft.KapibaraStudio.TestSupport"),
        };
        var benchmarkTestsRoot = Path.Combine(repositoryRoot, "tests", "EsilvaSoft.KapibaraStudio.Benchmarks");
        foreach (var root in unitTestRoots)
            Assert.That(Directory.Exists(root), Is.True, "Diretório de testes não encontrado: " + root);
        Assert.That(Directory.Exists(benchmarkTestsRoot), Is.True, "Projeto de benchmarks não encontrado: " + benchmarkTestsRoot);

        // Benchmarks shares an executable project with NUnit fixtures. Scan only the conventional *Tests.cs unit
        // sources here; BenchmarkDotNet fixtures and explicitly run integration tools have separate resource rules.
        var testFiles = unitTestRoots.SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(benchmarkTestsRoot, "*Tests.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        var offenders = new List<string>();

        foreach (var file in testFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var (category, pattern) in ForbiddenCalls)
                {
                    if (pattern.IsMatch(lines[index]))
                        offenders.Add($"{Path.GetRelativePath(repositoryRoot, file)}:{index + 1} [{category}]");
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "Suítes unitárias e TestSupport devem usar ports/doubles; acesso ao host pertence a IntegrationTests:\n"
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// CI never downloads models. A Hub source without a fake HttpMessageHandler reaches huggingface.co, so it may only
    /// exist in [Explicit] tests of the LocalModelIntegration category, which CI excludes besides never selecting them.
    /// </summary>
    [Test]
    public void RealModelHubAccessIsConfinedToExplicitLocalModelTests()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testFiles = TestProjects
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(repositoryRoot, "tests", project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                // This guard quotes the constructor in its own samples.
                && Path.GetFileName(path) != nameof(UnitSystemBoundaryIntegrationTests) + ".cs");
        var offenders = new List<string>();

        foreach (var file in testFiles)
        {
            var lines = File.ReadAllLines(file);
            var fixtureIsLocalModel = lines.Any(line => line.StartsWith('[') && line.Contains(LocalModelCategory, StringComparison.Ordinal));
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("new HuggingFaceModelSource(", StringComparison.Ordinal)
                    || UsesFakeHandler(StatementFrom(lines, index)))
                    continue;
                var attributes = MemberAttributes(lines, index);
                if (!attributes.Contains("Explicit(", StringComparison.Ordinal)
                    || !(fixtureIsLocalModel || attributes.Contains(LocalModelCategory, StringComparison.Ordinal)))
                    offenders.Add($"{Path.GetRelativePath(repositoryRoot, file)}:{index + 1}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "Acesso real ao Hugging Face exige [Explicit] e Category(\"LocalModelIntegration\"); o CI nunca baixa modelos:\n"
            + string.Join(Environment.NewLine, offenders));
    }

    [TestCase("new HuggingFaceModelSource(storage, handler: handler);", true)]
    [TestCase("new HuggingFaceModelSource(storage, repositories, handler, new Uri(\"https://stage.test/\"));", true)]
    [TestCase("new HuggingFaceModelSource(storage);", false)]
    [TestCase("new HuggingFaceModelSource(storage, [repository]);", false)]
    public void RealModelHubGuardRecognizesFakeHandlers(string statement, bool fake)
    {
        Assert.That(UsesFakeHandler(statement), Is.EqualTo(fake));
    }

    private static readonly string[] TestProjects =
        ["EsilvaSoft.KapibaraStudio.UnitTests", "EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests", "EsilvaSoft.KapibaraStudio.IntegrationTests"];
    private const string LocalModelCategory = "Category(\"LocalModelIntegration\")";
    private static readonly Regex HandlerArgument = new(@"\bhandler\b", RegexOptions.CultureInvariant);

    private static bool UsesFakeHandler(string statement) => HandlerArgument.IsMatch(statement);

    private static string StatementFrom(string[] lines, int index)
    {
        var end = index;
        while (end < lines.Length - 1 && !lines[end].Contains(';', StringComparison.Ordinal)) end++;
        return string.Join(' ', lines[index..(end + 1)]);
    }

    /// <summary>Attribute lines directly above the enclosing member (members are indented by four spaces).</summary>
    private static string MemberAttributes(string[] lines, int index)
    {
        var member = index;
        while (member >= 0 && !Regex.IsMatch(lines[member], @"^ {4}(?:public|private|internal|protected)\b", RegexOptions.CultureInvariant)) member--;
        var first = member;
        while (first > 0 && lines[first - 1].TrimStart().StartsWith('[')) first--;
        return member < 0 ? "" : string.Join(' ', lines[first..member]);
    }

    [TestCase("Environment.GetEnvironmentVariable")]
    [TestCase("Environment.GetEnvironmentVariable(\"HOME\")")]
    [TestCase("AppContext.BaseDirectory")]
    [TestCase("Path.GetFullPath(workspace)")]
    public void HostStateGuardFindsEnvironmentMethodGroupsAndImplicitCurrentDirectory(string source)
    {
        Assert.That(ForbiddenCalls["host"].IsMatch(source), Is.True);
    }

    [Test]
    public void HostStateGuardAllowsPathResolutionWithAnExplicitBase()
    {
        Assert.That(ForbiddenCalls["host"].IsMatch("Path.GetFullPath(path, capturedBase)"), Is.False);
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
