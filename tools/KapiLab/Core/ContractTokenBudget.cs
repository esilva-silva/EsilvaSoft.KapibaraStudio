using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class ContractTokenBudget
{
    internal static int EffectiveContextTokens(int requestedContextTokens, int requestedCompletionTokens,
        int effectiveContextLength, int effectiveAutocompleteMaximumTokens)
    {
        return AutocompleteTokenBudget.EffectiveContextTokens(requestedContextTokens, requestedCompletionTokens,
            effectiveContextLength, effectiveAutocompleteMaximumTokens);
    }
}
