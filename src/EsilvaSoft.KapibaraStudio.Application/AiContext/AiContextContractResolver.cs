using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext;

/// <summary>
/// Picks the <see cref="IAiContextContract"/> a local model package expects. Second line of defence only: the
/// user-facing compatibility verdict (and its message) is produced by <c>LocalModelCatalog.Validate</c> when the
/// package is read, per <c>DEC-A31C-CONTEXTCONTRACT</c>. By the time a request reaches this resolver an unknown
/// contract is a bug in the calling path, not user input, so the resolver refuses instead of degrading: serving the
/// v1 prompt to a model trained on another format produces silently worse output.
/// </summary>
/// <remarks>
/// Only <see cref="EditorContextV1Contract"/> exists today. <c>LocalModelMetadata.SupportsRepositoryContext</c>
/// deliberately does not take part in the choice: it describes how much context a model can exploit, not how the
/// prompt is serialized, and the contracts that consume it do not exist yet (batch A34b).
/// </remarks>
public static class AiContextContractResolver
{
    /// <summary>Contracts implemented by this build, keyed by canonical identifier.</summary>
    public static IReadOnlyList<IAiContextContract> Implemented { get; } = [EditorContextV1Contract.Instance];

    /// <summary>Contract for a package; a package that declares none gets the frozen <c>editor-context-v1</c>.</summary>
    /// <exception cref="AiContextContractNotSupportedException">The metadata declares a contract this build does not implement.</exception>
    public static IAiContextContract Resolve(LocalModelMetadata? metadata) => Resolve(metadata?.ContextContract);

    /// <summary>Contract for a declared identifier; <see langword="null"/> means undeclared, never incompatible.</summary>
    /// <exception cref="AiContextContractNotSupportedException">This build does not implement <paramref name="contextContract"/>.</exception>
    public static IAiContextContract Resolve(string? contextContract) => TryResolve(contextContract, out var contract)
        ? contract!
        : throw new AiContextContractNotSupportedException(contextContract!, LocalModelContextContracts.Supported);

    /// <summary>Non-throwing form, for callers that already own the compatibility verdict.</summary>
    public static bool TryResolve(string? contextContract, out IAiContextContract? contract)
    {
        var canonical = LocalModelContextContracts.Resolve(contextContract);
        contract = canonical is null
            ? null
            : Implemented.FirstOrDefault(known => string.Equals(known.ContractId, canonical, StringComparison.Ordinal));
        return contract is not null;
    }
}

/// <summary>A model package asked for a prompt contract this build cannot serialize.</summary>
public sealed class AiContextContractNotSupportedException : InvalidOperationException
{
    public AiContextContractNotSupportedException() { }
    public AiContextContractNotSupportedException(string message) : base(message) { }
    public AiContextContractNotSupportedException(string message, Exception? innerException) : base(message, innerException) { }
    public AiContextContractNotSupportedException(string declaredContract, IReadOnlyList<string> supported)
        : base(Format(declaredContract, supported)) => DeclaredContract = declaredContract;

    /// <summary>The identifier declared by the package; empty when the exception was built without one.</summary>
    public string DeclaredContract { get; } = "";

    private static string Format(string declaredContract, IReadOnlyList<string> supported)
        => $"Contrato de contexto não implementado por esta versão: '{declaredContract}'. Implementados: {string.Join(", ", supported)}.";
}
