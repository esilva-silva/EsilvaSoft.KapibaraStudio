using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

public sealed record AiProviderCandidate(AiAccelerationMode Kind, string Provider, string GenAiName, string? Device);
