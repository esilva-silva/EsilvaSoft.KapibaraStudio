using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class EnvironmentReportTests
{
    [Test]
    public void MissingRepositoryDoesNotClaimIdeIsCleanAndStillFingerprintsTheKapiLabAssembly()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "kapilab-env-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var report = EnvironmentReport.Create(workspace);

            Assert.Multiple(() =>
            {
                Assert.That(report.Schema, Is.EqualTo("kapilab-env-v2"));
                Assert.That(report.IdeCommit, Is.Null);
                Assert.That(report.IdeDirty, Is.Null,
                    "A packaged executable without a discoverable .git directory cannot claim the IDE checkout is clean.");
                Assert.That(report.KapiLabAssemblySha256, Does.Match("^[0-9a-f]{64}$"));
            });
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
