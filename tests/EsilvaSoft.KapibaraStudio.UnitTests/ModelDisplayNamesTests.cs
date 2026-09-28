using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed class ModelDisplayNamesTests
{
    [TestCase("int4", AiAccelerationMode.Cpu, null, "INT4")]
    [TestCase("int8", AiAccelerationMode.Cpu, null, "INT8")]
    [TestCase("dml-fp16", AiAccelerationMode.Gpu, "DirectML", "FP16")]
    [TestCase("DML-INT4", AiAccelerationMode.Gpu, "DirectML", "INT4")]
    public void VariantFoldersDescribeHardwareAndPrecision(string folder, AiAccelerationMode hardware, string? provider, string precision) =>
        Assert.That(ModelDisplayNames.ParseVariant(folder), Is.EqualTo(new ModelFlavor(hardware, provider, precision)));

    [TestCase("payload")]
    [TestCase("int3")]
    [TestCase("npu-int4")]
    [TestCase("dml-fp16-extra")]
    public void UnknownFoldersAreNotGuessed(string folder) => Assert.That(ModelDisplayNames.ParseVariant(folder), Is.Null);

    [Test]
    public void InstalledModelsUseFolderConventionThenMetadataAndOtherwiseKeepTheirName()
    {
        Assert.That(ModelDisplayNames.ForInstalled("SlopCoder-Mongo-1.5B-full-ONNX-dml-fp16", null), Is.EqualTo("SlopCoder-Mongo-1.5B-full — GPU DirectML FP16"));
        Assert.That(ModelDisplayNames.ForInstalled("SlopCoder-Mongo-1.5B-full-ONNX-INT8", null), Is.EqualTo("SlopCoder-Mongo-1.5B-full — CPU INT8"),
            "Folders copied by hand before the download feature follow the same convention.");
        Assert.That(ModelDisplayNames.ForInstalled("meu-modelo", new LocalModelMetadata { Name = "SlopCoder-Mongo-0.5B ONNX DML-INT4", Hardware = [AiAccelerationMode.Gpu] }),
            Is.EqualTo("SlopCoder-Mongo-0.5B — GPU DirectML INT4"));
        Assert.That(ModelDisplayNames.ForInstalled("meu-modelo", new LocalModelMetadata { Name = "Coder ONNX INT4", Hardware = [AiAccelerationMode.Gpu] }),
            Is.EqualTo("Coder — GPU INT4"), "Declared hardware wins when the name has no provider.");
        Assert.That(ModelDisplayNames.ForInstalled("Coder-1.5B", new LocalModelMetadata { Name = "Coder 1.5B" }), Is.Null);
        Assert.That(ModelDisplayNames.ForInstalled("Qwen2.5-Coder-1.5B", null), Is.Null);
    }
}
