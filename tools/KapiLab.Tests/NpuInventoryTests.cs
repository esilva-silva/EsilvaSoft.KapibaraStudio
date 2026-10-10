using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class NpuInventoryTests
{
    [Test]
    public void MissingNpuIsReportedAsUnavailableWithoutHidingCpu()
    {
        var report = NpuInventoryReport.Create([
            new(AiAccelerationMode.Cpu, "CPU", "CPU", true),
            new(AiAccelerationMode.Npu, "", "NPU", false) { Reason = "Provider ausente." }
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(report.Schema, Is.EqualTo("kapilab-npu-inventory-v1"));
            Assert.That(report.NpuAvailable, Is.False);
            Assert.That(report.NpuStatus, Is.EqualTo("Provider ausente."));
            Assert.That(report.Devices.Single(device => device.Kind == nameof(AiAccelerationMode.Cpu)).IsAvailable, Is.True);
        });
    }

    [Test]
    public void AvailableNpuHasNoUnavailableReason()
    {
        var report = NpuInventoryReport.Create([new(AiAccelerationMode.Npu, "QNN", "NPU fixture", true)]);

        Assert.Multiple(() =>
        {
            Assert.That(report.NpuAvailable, Is.True);
            Assert.That(report.NpuStatus, Is.Null);
        });
    }
}
