using System.Globalization;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>
/// Decide, sem tocar no runtime nativo, se um pedido pode reaproveitar o KV de uma sessão e sob qual chave.
/// O gate do produto é o mesmo para todos os usos: CPU, opt-in nas preferências e homologação declarada no pacote.
/// </summary>
internal static class PrefixReusePolicy
{
    /// <summary>
    /// Autocomplete só reaproveita em geração gulosa. Os passos de um turno de agente (prompt de chat) podem amostrar:
    /// a amostragem só afeta os tokens gerados depois do prefixo reaproveitado, e esses são descartados pelo retorno
    /// ao prefixo comum do passo seguinte.
    /// </summary>
    internal static bool IsEligible(AiAccelerationMode provider, ModelGenerationRequest request, PrefixCacheMode? settingsMode,
        bool packageValidated, bool allowUnvalidated)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (provider != AiAccelerationMode.Cpu || settingsMode != PrefixCacheMode.Reuse) return false;
        if (request.PrefixCache is not { Mode: PrefixCacheMode.Reuse, SessionKey.Length: > 0 }) return false;
        if (!allowUnvalidated && !packageValidated) return false;
        return request.ChatPrompt is not null
            || request.Temperature == 0 && request.TopP is null && request.TopK is null;
    }

    /// <summary>
    /// Chave efetiva da sessão. Um gerador nativo conserva os parâmetros de busca com que foi criado; passos de chat
    /// com amostragem diferente (com e sem raciocínio) usam geradores distintos em vez de herdar a amostragem errada.
    /// </summary>
    internal static string SessionKey(ModelGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.PrefixCache?.SessionKey ?? "";
        if (request.ChatPrompt is null || key.Length == 0) return key;
        return string.Create(CultureInfo.InvariantCulture,
            $"{key}|{request.Temperature:R}|{request.TopP?.ToString("R", CultureInfo.InvariantCulture)}|{request.TopK}|{request.Seed}");
    }
}
