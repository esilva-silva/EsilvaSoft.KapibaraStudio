using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class MongoshProcessAdapterTests
{
    private static readonly string[] ExpectedArguments = ["--norc", "--nodb", "--file", "fixture.js"];

    [Test]
    public void CredentialsAndEnvironmentValuesUseProcessEnvironmentAndNeverArgumentsOrSource()
    {
        var request = new MongoshProcessRequest("mongodb://user:secret-canary@localhost",
            MongoshScriptTemplate.BuildScript("{}", "slop.results.emit(ENV.get('key'));", "fixture"),
            new Dictionary<string, string> { ["key"] = "secret-canary" });
        var info = LocalMongoshScriptProcessRunner.BuildStartInfo(request, "fixture.js");

        Assert.Multiple(() =>
        {
            Assert.That(info.ArgumentList, Is.EqualTo(ExpectedArguments));
            Assert.That(string.Join(" ", info.ArgumentList), Does.Not.Contain("secret-canary"));
            Assert.That(request.ScriptSource, Does.Not.Contain("secret-canary"));
            Assert.That(info.Environment["SLOP_CONNECTION_URI"], Is.EqualTo(request.ConnectionString));
            Assert.That(JsonSerializer.Deserialize<Dictionary<string, string>>(info.Environment["SLOP_ENVIRONMENT_VALUES"]!)!["key"], Is.EqualTo("secret-canary"));
            Assert.That(info.UseShellExecute, Is.False);
            Assert.That(info.CreateNoWindow, Is.True);
            Assert.That(info.RedirectStandardOutput && info.RedirectStandardError, Is.True);
            Assert.That(info.StandardOutputEncoding, Is.EqualTo(Encoding.UTF8));
            Assert.That(info.StandardErrorEncoding, Is.EqualTo(Encoding.UTF8));
        });
    }

    [Test]
    public void CancellationBeforeAcquisitionDoesNotTryToDiscoverOrStartMongosh()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new LocalMongoshScriptProcessRunner();
        Assert.CatchAsync<OperationCanceledException>(() => runner.RunAsync(new("mongodb://localhost", "1", new Dictionary<string, string>()), cancellation.Token));
    }
}
