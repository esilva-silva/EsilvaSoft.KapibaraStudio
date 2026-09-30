using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.AiContext.Experimental;

/// <summary>
/// Prova viva do estado pretendido do lote A34b: os quatro formatos <b>existem e são testáveis</b>, e <b>nenhuma rota
/// de produção os alcança</b>. Se algum dia um deles for promovido, é aqui que o teste falha primeiro e obriga a
/// decisão a ser explícita (registro em <c>LocalModelContextContracts.Supported</c> + resolver + relatório de A34c).
/// </summary>
[TestFixture]
public sealed class ExperimentalContextReachabilityTests
{
    public static IEnumerable<string> ContractIds() => ExperimentalContextContracts.All.Select(contract => contract.ContractId);

    [Test]
    public void TheFourFormatsExistAndAreDistinct()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExperimentalContextContracts.All, Has.Count.EqualTo(4));
            Assert.That(ContractIds().ToArray(), Is.Unique);
            Assert.That(ExperimentalContextContracts.All.Select(contract => contract.GetType()), Is.Unique);
            Assert.That(ContractIds(), Is.All.StartWith(ExperimentalContextContract.IdentifierPrefix));
            Assert.That(ContractIds().ToArray(), Is.Ordered.Using<string>(StringComparer.Ordinal));
            Assert.That(ExperimentalContextContracts.All.Select(contract => contract.Hypothesis), Is.All.Not.Empty);
        });
    }

    /// <summary>Nenhum identificador experimental pode ser confundido com o contrato congelado.</summary>
    [TestCaseSource(nameof(ContractIds))]
    public void ContractIdIsNeverTheFrozenV1(string contractId)
        => Assert.That(contractId, Is.Not.EqualTo(LocalModelContextContracts.EditorContextV1).IgnoreCase);

    /// <summary>O caminho real de um pacote: <c>kapibarastudio-model.json</c> declarando o formato é rejeitado.</summary>
    [TestCaseSource(nameof(ContractIds))]
    public void DeclaringTheContractInAPackageIsRefused(string contractId)
    {
        var error = Assert.Throws<AiContextContractNotSupportedException>(
            () => AiContextContractResolver.Resolve(new LocalModelMetadata { ContextContract = contractId }));
        Assert.Multiple(() =>
        {
            Assert.That(error!.DeclaredContract, Is.EqualTo(contractId));
            Assert.That(error.Message, Does.Contain(LocalModelContextContracts.EditorContextV1));
            Assert.That(AiContextContractResolver.TryResolve(contractId, out var resolved), Is.False);
            Assert.That(resolved, Is.Null);
            Assert.That(LocalModelContextContracts.IsSupported(contractId), Is.False);
            Assert.That(LocalModelContextContracts.Resolve(contractId), Is.Null);
        });
    }

    [TestCaseSource(nameof(ContractIds))]
    public void ContractIsNeitherSupportedNorImplemented(string contractId)
    {
        Assert.Multiple(() =>
        {
            Assert.That(LocalModelContextContracts.Supported, Does.Not.Contain(contractId));
            Assert.That(AiContextContractResolver.Implemented.Select(contract => contract.ContractId), Does.Not.Contain(contractId));
            Assert.That(AiContextContractResolver.Implemented.Select(contract => contract.GetType()),
                Has.None.Matches<Type>(type => typeof(ExperimentalContextContract).IsAssignableFrom(type)));
        });
    }

    /// <summary>
    /// Nenhum tipo de produção (fora do próprio namespace experimental) declara membro algum destes tipos: sem
    /// referência estática não há como um caminho de produção construir um destes contratos.
    /// </summary>
    [Test]
    public void NoProductionTypeReferencesTheExperimentalContracts()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var experimental = typeof(ExperimentalContextContract).Namespace!;
        var offenders = typeof(AutocompleteContextBuilder).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(experimental, StringComparison.Ordinal) != true)
            .SelectMany(type => type.GetFields(all).Select(field => (type, used: field.FieldType))
                .Concat(type.GetProperties(all).Select(property => (type, used: property.PropertyType)))
                .Concat(type.GetMethods(all).SelectMany(method => method.GetParameters()
                    .Select(parameter => parameter.ParameterType).Append(method.ReturnType)).Select(used => (type, used)))
                .Concat(type.GetConstructors(all).SelectMany(constructor => constructor.GetParameters()).Select(parameter => (type, used: parameter.ParameterType))))
            .Where(pair => pair.used.Namespace?.StartsWith(experimental, StringComparison.Ordinal) == true)
            .Select(pair => pair.type.FullName + " → " + pair.used.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.That(offenders, Is.Empty, "Tipo de produção referenciando formato experimental: " + string.Join(", ", offenders));
    }

}
