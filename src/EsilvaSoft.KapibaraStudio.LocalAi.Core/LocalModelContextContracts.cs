namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Prompt contracts this build knows how to serialize. The declaration in kapibarastudio-model.json is optional:
/// a package that does not declare one is assumed to expect <see cref="EditorContextV1"/>, the frozen contract
/// the SlopCoder packages were trained on. Matching is in-memory only and never loads weights.
/// </summary>
public static class LocalModelContextContracts
{
    /// <summary>Frozen production contract (AutocompleteContextBuilder header).</summary>
    public const string EditorContextV1 = "editor-context-v1";

    /// <summary>
    /// v1 plus the <c>TARGET FIELDS</c> header line and inner-closer post-processing (cursor-in-call students v4).
    /// Unknown to older builds, which mark such a package invalid instead of serving it the v1 prompt.
    /// </summary>
    public const string EditorContextV2 = "editor-context-v2";

    public static IReadOnlyList<string> Supported { get; } = [EditorContextV1, EditorContextV2];

    /// <summary>True for an undeclared contract (absence is never an incompatibility) and for every known identifier.</summary>
    public static bool IsSupported(string? contract) => contract is null || Resolve(contract) is not null;

    /// <summary>Canonical identifier for a declared contract, or null when this build does not implement it.</summary>
    public static string? Resolve(string? contract) => contract is null
        ? EditorContextV1
        : Supported.FirstOrDefault(known => string.Equals(known, contract.Trim(), StringComparison.OrdinalIgnoreCase));
}
