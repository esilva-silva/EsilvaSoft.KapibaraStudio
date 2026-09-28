using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class MongoshScriptExecutionServiceFailureTests
{
    private const string SecretUri = "mongodb://user:secret-canary@host/db";

    [Test]
    public void FailedProcessRedactsCredentialsButKeepsDiagnosticAndDropsResults()
    {
        var stdout = "__SLOPDATAADMIN_RESULT__" + SecretUri + "\n" + "hostile-output " + SecretUri;
        var stderr = "connection failed: " + SecretUri + "\n" + "hostile-error";

        var result = MongoshScriptExecutionService.CreateResult(17, stdout, stderr, TimeSpan.FromSeconds(1));
        var serialized = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(17));
            Assert.That(result.Results, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain("hostile-output"));
            Assert.That(result.StandardError, Does.Contain("17").And.Contain("connection failed").And.Contain("hostile-error"));
            Assert.That(serialized, Does.Not.Contain("secret-canary").And.Not.Contain(SecretUri));
        });
    }

    [Test]
    public void SyntaxFailureMessageIsPreserved()
    {
        const string stderr = "SyntaxError: Unexpected token, expected \",\" (3:14)\n> 3 | db.c.find({a: 1 b: 2})";

        var result = MongoshScriptExecutionService.CreateResult(1, "", stderr, TimeSpan.Zero);

        Assert.That(result.StandardError, Does.Contain("SyntaxError: Unexpected token").And.Contain("(3:14)")
            .And.Contain("db.c.find"));
    }

    [Test]
    public void KnownSecretsInlinePasswordsAndOptionsAreRedactedOnBothStreams()
    {
        var stdout = "log p4ss/w0rd and password=hunter2 and user:plain@host";
        var stderr = "auth failed for p4ss%2Fw0rd; mongodb+srv://a:b@cluster.example/?pwd=zzz";

        var result = MongoshScriptExecutionService.CreateResult(2, stdout, stderr, TimeSpan.Zero, ["p4ss/w0rd"]);
        var text = result.StandardOutput + "|" + result.StandardError;

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("p4ss").And.Not.Contain("hunter2").And.Not.Contain("a:b@")
                .And.Not.Contain("zzz").And.Not.Contain("cluster.example"));
            Assert.That(text, Does.Contain("auth failed").And.Contain("[redigido]"));
        });
    }

    [Test]
    public void SuccessfulProcessKeepsResultsAndSanitizesStderr()
    {
        var stdout = "__SLOPDATAADMIN_RESULT__{\"a\":1}\nhello";
        var stderr = "warning using " + SecretUri;

        var result = MongoshScriptExecutionService.CreateResult(0, stdout, stderr, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Results[0], Is.EqualTo("{\"a\":1}"));
            Assert.That(result.StandardOutput, Is.EqualTo("hello"));
            Assert.That(result.StandardError, Does.StartWith("warning using").And.Not.Contain("secret-canary"));
        });
    }

    [Test]
    public void VaultReferencesAreRedacted()
    {
        var id = Guid.NewGuid();
        var result = MongoshScriptExecutionService.CreateResult(3, "", $"cannot resolve {{{{vault:db}}}} ref {id}", TimeSpan.Zero);

        Assert.That(result.StandardError, Does.Not.Contain("vault:db").And.Not.Contain(id.ToString()));
    }

    [Test]
    public void LongOutputIsBounded()
    {
        var result = MongoshScriptExecutionService.CreateResult(1, new string('x', 500_000), new string('e', 500_000), TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(result.StandardOutput.Length, Is.LessThan(MongoshScriptExecutionService.MaxDiagnosticCharacters + 100));
            Assert.That(result.StandardError.Length, Is.LessThan(MongoshScriptExecutionService.MaxDiagnosticCharacters + 200));
            Assert.That(result.StandardError, Does.Contain("truncada"));
        });
    }
}
