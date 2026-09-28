using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Hardware and precision of an ONNX GenAI export.</summary>
public sealed record ModelFlavor(AiAccelerationMode Hardware, string? Provider, string Precision);
