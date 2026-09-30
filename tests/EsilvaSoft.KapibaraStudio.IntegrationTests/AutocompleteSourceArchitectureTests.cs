using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class AutocompleteSourceArchitectureTests
{
    /// <summary>O núcleo de IA local não pode redeclarar o vocabulário do domínio Mongo em seus fontes.</summary>
    [Test]
    public void LocalAiCoreNeverDeclaresMongoVocabulary()
    {
        // E nenhum fonte pode redeclarar localmente o vocabulário do domínio Mongo, que é o outro jeito de trazer a
        // semântica para dentro sem precisar de referência de assembly.
        string[] domainVocabulary = ["CompletionContext", "MongoSyntaxTree", "AiFact", "CollectionSchema",
            "AutocompleteRequest", "LanguageDefinition", "SymbolKinds", "EditorDialects", "MongoDB"];
        var directory = Path.Combine(RepositoryRoot(), "src", "EsilvaSoft.KapibaraStudio.LocalAi.Core");
        var sources = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
        var byText = sources.SelectMany(file => domainVocabulary
            .Where(word => File.ReadAllText(file).Contains(word, StringComparison.Ordinal))
            .Select(word => Path.GetFileName(file) + " -> " + word)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(sources, Is.Not.Empty, "O diretório de LocalAi.Core precisa estar sendo varrido.");
            Assert.That(byText, Is.Empty, "Vocabulário de domínio Mongo dentro de LocalAi.Core: " + string.Join(", ", byText));
        });
    }

    /// <summary>
    /// Regra 9 da meta, e o invariante mais caro de violar do repositório: um segundo <c>LiteDatabase</c> sobre o
    /// mesmo arquivo em modo <c>Direct</c> falha em tempo de execução, no computador do usuário, com a sessão dele
    /// dentro. A varredura é textual de propósito — o dono da instância é um detalhe de um construtor e não aparece
    /// como membro em metadados de tipo, e é exatamente numa construção nova, em qualquer arquivo, que o erro
    /// apareceria.
    /// </summary>
    [Test]
    public void OnlyOneLiteDatabaseIsEverConstructed()
    {
        var offenders = ProductionSourceFiles()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(entry => entry.line.Contains("new LiteDatabase(", StringComparison.Ordinal))
            .Select(entry => Path.GetFileName(entry.file) + ":" + entry.number.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        Assert.That(offenders, Has.Length.EqualTo(1),
            "Exatamente uma construção de LiteDatabase é permitida (o proprietário registrado em DI). Encontradas: "
            + string.Join(", ", offenders));
    }

    /// <summary>Raiz do repositório, para as regras que precisam ler o código-fonte de produção em vez de metadados.</summary>
    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.KapibaraStudio.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Raiz do repositório não encontrada.");
        return root!.FullName;
    }

    /// <summary>Todos os .cs de produção, sem os intermediários gerados em obj/bin.</summary>
    private static string[] ProductionSourceFiles()
    {
        var separator = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(separator + "obj" + separator, StringComparison.Ordinal)
                && !file.Contains(separator + "bin" + separator, StringComparison.Ordinal))
            .ToArray();
        Assert.That(files, Is.Not.Empty, "Nenhum fonte de produção encontrado; o teste varreria o vazio.");
        return files;
    }

}
