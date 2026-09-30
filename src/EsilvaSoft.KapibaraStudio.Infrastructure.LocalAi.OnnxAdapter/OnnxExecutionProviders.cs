using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

public static class OnnxExecutionProviders
{
    public static IReadOnlyList<OnnxExecutionProviderDescriptor> Known { get; } =
    [
        new("CPUExecutionProvider", "CPU", "cpu", AiAccelerationMode.Cpu, AiExecutionProvider.Cpu),
        new("DmlExecutionProvider", "DirectML", "dml", AiAccelerationMode.Gpu, AiExecutionProvider.DirectML),
        new("CUDAExecutionProvider", "CUDA", "cuda", AiAccelerationMode.Gpu, AiExecutionProvider.Cuda),
        new("QNNExecutionProvider", "QNN", "qnn", AiAccelerationMode.Npu, AiExecutionProvider.Qnn),
        new("OpenVINOExecutionProvider", "OpenVINO", "OpenVINO", AiAccelerationMode.Npu, AiExecutionProvider.OpenVino),
        new("VitisAIExecutionProvider", "VitisAI", "VitisAI", AiAccelerationMode.Npu, null)
    ];

    public static OnnxExecutionProviderDescriptor? ByDisplayName(string name) => Known.FirstOrDefault(descriptor => descriptor.DisplayName == name);
}
