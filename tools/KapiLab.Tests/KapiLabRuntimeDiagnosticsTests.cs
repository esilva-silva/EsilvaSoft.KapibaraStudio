using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class KapiLabRuntimeDiagnosticsTests
{
    [TestCase("provider.load.failed", "provider-load")]
    [TestCase("tokenizer.load.failed", "tokenizer-initialization")]
    [TestCase("provider.generation.failed", "provider-generation")]
    public void RetainsOnlyKnownFailureStage(string eventName, string expectedStage)
    {
        var diagnostics = new KapiLabRuntimeDiagnostics();

        diagnostics.Record(eventName, "C:\\private\\model\\secret prompt text", TimeSpan.FromSeconds(1));

        Assert.That(diagnostics.FailureStage, Is.EqualTo(expectedStage));
    }

    [TestCase("provider.config.creating", "provider-config-create")]
    [TestCase("provider.clearing", "provider-clear")]
    [TestCase("provider.appending", "provider-append")]
    [TestCase("provider.overlaying", "provider-overlay")]
    [TestCase("provider.model.creating", "model-create")]
    public void ReportsOnlyTheFixedCurrentLoadStage(string eventName, string expectedStage)
    {
        var diagnostics = new KapiLabRuntimeDiagnostics();
        diagnostics.Record(eventName, "C:\\private\\model\\secret prompt text");

        diagnostics.Record("provider.load.failed", "native detail and path");

        Assert.That(diagnostics.FailureStage, Is.EqualTo(expectedStage));
    }

    [Test]
    public void IgnoresUnknownEventAndItsDetail()
    {
        var diagnostics = new KapiLabRuntimeDiagnostics();
        diagnostics.Record("model.ready", "dml");

        diagnostics.Record("arbitrary.event", "C:\\private\\model\\secret prompt text");

        Assert.That(diagnostics.FailureStage, Is.Null);
    }
}
