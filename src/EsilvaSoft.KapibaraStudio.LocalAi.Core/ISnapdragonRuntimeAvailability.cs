namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Positive build and physical-device evidence; registering an EP alone is insufficient.</summary>
public interface ISnapdragonRuntimeAvailability
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
