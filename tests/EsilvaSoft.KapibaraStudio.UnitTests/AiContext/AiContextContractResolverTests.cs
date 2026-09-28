using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.AiContext;

/// <summary>
/// Second line of defence for the prompt contract: the user-facing verdict belongs to
/// <c>LocalModelCatalog.Validate</c> (DEC-A31C-CONTEXTCONTRACT); here the only requirement is that an unknown
/// declaration is refused in a typed way instead of silently falling back to the v1 prompt.
/// </summary>
[TestFixture]
public sealed class AiContextContractResolverTests
{
    [Test]
    public void UndeclaredContractResolvesToTheFrozenV1()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AiContextContractResolver.Resolve((LocalModelMetadata?)null), Is.TypeOf<EditorContextV1Contract>());
            Assert.That(AiContextContractResolver.Resolve(new LocalModelMetadata()), Is.TypeOf<EditorContextV1Contract>());
            Assert.That(AiContextContractResolver.Resolve((string?)null), Is.TypeOf<EditorContextV1Contract>());
        });
    }

    [TestCase("editor-context-v1")]
    [TestCase("Editor-Context-V1")]
    [TestCase("  editor-context-v1  ")]
    public void DeclaredV1ResolvesRegardlessOfCaseAndPadding(string declared)
        => Assert.That(AiContextContractResolver.Resolve(new LocalModelMetadata { ContextContract = declared }).ContractId,
            Is.EqualTo(LocalModelContextContracts.EditorContextV1));

    [TestCase("compact-facts-v1")]
    [TestCase("editor-context-v2")]
    [TestCase("")]
    public void UnknownContractFailsTyped(string declared)
    {
        var metadata = new LocalModelMetadata { ContextContract = declared };
        var error = Assert.Throws<AiContextContractNotSupportedException>(() => AiContextContractResolver.Resolve(metadata));
        Assert.Multiple(() =>
        {
            Assert.That(error!.DeclaredContract, Is.EqualTo(declared));
            Assert.That(error.Message, Does.Contain(LocalModelContextContracts.EditorContextV1));
            Assert.That(AiContextContractResolver.TryResolve(declared, out var contract), Is.False);
            Assert.That(contract, Is.Null);
        });
    }

    [Test]
    public void EveryImplementedContractDeclaresASupportedIdentifier()
        => Assert.That(AiContextContractResolver.Implemented.Select(contract => contract.ContractId),
            Is.EquivalentTo(LocalModelContextContracts.Supported));

    /// <summary>
    /// <c>supportsRepositoryContext</c> describes what a model can exploit, not how the prompt is serialized, so it
    /// must not steer the choice while v1 is the only implemented contract.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void RepositoryContextSupportDoesNotChangeTheChosenContract(bool supports)
        => Assert.That(AiContextContractResolver.Resolve(new LocalModelMetadata { SupportsRepositoryContext = supports }),
            Is.TypeOf<EditorContextV1Contract>());
}
