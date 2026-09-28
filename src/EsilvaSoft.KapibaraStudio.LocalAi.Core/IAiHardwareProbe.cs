using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Backends the runtime of this build exposes; the UI never offers hardware that is not reported here.</summary>
public interface IAiHardwareProbe
{
    Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default);
}
