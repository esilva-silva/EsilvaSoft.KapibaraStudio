using EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.AiContext.Experimental;

[TestFixture]
[Category("Integration")]
public sealed class ExperimentalContextSourceReachabilityTests
{
    private static IEnumerable<string> ContractIds() => ExperimentalContextContracts.All.Select(contract => contract.ContractId);

    /// <summary>
    /// Nenhum <c>ServiceCollectionExtensions</c> do produto cita os formatos. O teste lê o próprio código-fonte: um
    /// contêiner de DI real não tem como provar ausência (resolver um tipo não registrado devolve nulo tanto para o
    /// que nunca existiu quanto para o que foi registrado sob outra interface), e montar o contêiner completo exigiria
    /// abrir o LiteDB do workspace. A leitura do fonte é barata, determinística e falha no instante em que alguém
    /// registrar qualquer coisa do namespace experimental.
    /// </summary>
    [Test]
    public void NoServiceCollectionExtensionMentionsTheExperimentalFormats()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src"), "*ServiceCollectionExtensions.cs", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty, "Nenhum ServiceCollectionExtensions encontrado; o teste varreria o vazio.");
        var offenders = files.Where(file =>
        {
            var text = File.ReadAllText(file);
            return text.Contains("Experimental", StringComparison.Ordinal)
                || ContractIds().Any(id => text.Contains(id, StringComparison.Ordinal));
        }).ToArray();
        Assert.That(offenders, Is.Empty, "Formato experimental citado em DI: " + string.Join(", ", offenders));
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.KapibaraStudio.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Raiz do repositório não encontrada.");
        return root!.FullName;
    }
}
