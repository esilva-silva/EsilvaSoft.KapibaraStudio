using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class BenchMatrixTests
{
    [Test]
    public void ParsesVersionlessCellEnvelopeAndOptionalExecutionProvider()
    {
        var cells = BenchMatrixCommand.Parse("""{"cells":[{"package":"Repos/model/exports/cpu-int8","hardware":"cpu","scenario":"cpu-int8"},{"package":"Repos/model/exports/dml-fp16","hardware":"gpu","ep":"dml","scenario":"dml-fp16"}]}""");
        Assert.That(cells, Has.Count.EqualTo(2));
        Assert.That(cells[0].Hardware, Is.EqualTo("cpu"));
        Assert.That(cells[1].Ep, Is.EqualTo("dml"));
    }

    [TestCase("{\"cells\":[]}")]
    [TestCase("{\"schema\":\"kapilab-bench-matrix-v1\",\"cells\":[]}")]
    [TestCase("{\"cells\":[{\"package\":\"../outside\",\"hardware\":\"cpu\",\"scenario\":\"x\"}]}")]
    [TestCase("{\"cells\":[{\"package\":\"Repos/model\",\"hardware\":\"auto\",\"scenario\":\"x\"}]}")]
    [TestCase("{\"cells\":[{\"package\":\"Repos/model\",\"hardware\":\"cpu\",\"scenario\":\"x\",\"unknown\":1}]}")]
    [TestCase("{\"cells\":[],\"Cells\":[]}")]
    [TestCase("{\"cells\":[{\"package\":\"Repos/model\",\"hardware\":\"cpu\",\"scenario\":\"x\",\"Hardware\":\"gpu\"}]}")]
    public void RejectsInvalidOrAmbiguousCells(string json) =>
        Assert.Throws<InvalidDataException>(() => BenchMatrixCommand.Parse(json));
}
