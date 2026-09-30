using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>An execution provider configurable through ONNX Runtime GenAI.</summary>
public sealed record OnnxExecutionProviderDescriptor(string OrtName, string DisplayName, string GenAiName, AiAccelerationMode Kind, AiExecutionProvider? Setting);
