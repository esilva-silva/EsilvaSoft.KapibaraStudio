using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class SecretStoreContractsTests
{
    [Test]
    public void SecretReferenceRejectsEmptyIdentityAndInvalidVersion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new SecretReference(Guid.Empty), Throws.ArgumentException);
            Assert.That(() => new SecretReference(Guid.NewGuid(), 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void SecretReferenceContainsOnlyOpaqueIdentityAndPositiveVersion()
    {
        var id = Guid.NewGuid();
        var reference = new SecretReference(id, 3);

        Assert.Multiple(() =>
        {
            Assert.That(reference.Id, Is.EqualTo(id));
            Assert.That(reference.Version, Is.EqualTo(3));
            Assert.That(reference.ToString(), Does.Not.Contain("credential-value"));
        });
    }

    [TestCase(SecretStoreFailureCode.Unavailable)]
    [TestCase(SecretStoreFailureCode.Locked)]
    [TestCase(SecretStoreFailureCode.Denied)]
    [TestCase(SecretStoreFailureCode.Cancelled)]
    [TestCase(SecretStoreFailureCode.NotFound)]
    [TestCase(SecretStoreFailureCode.Corrupt)]
    public void FailedSecretReadPreservesTypedCodeWithoutExposingSecretOrProviderMessage(SecretStoreFailureCode code)
    {
        const string secret = "credential-value-must-not-appear";
        const string providerMessage = "backend exception must not be carried";
        var result = SecretStoreResults.Failed<string>(code);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure?.Code, Is.EqualTo(code));
            Assert.That(result.ToString(), Does.Not.Contain(secret));
            Assert.That(result.ToString(), Does.Not.Contain(providerMessage));
            Assert.That(() => _ = result.Value, Throws.TypeOf<InvalidOperationException>()
                .With.Message.Not.Contains(secret));
        });
    }

    [Test]
    public void CancelledPromptIsTypedSeparatelyFromCallerCancellation()
    {
        var dismissedPrompt = SecretStoreOperationResult.Failed(SecretStoreFailureCode.Cancelled);
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(dismissedPrompt.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Cancelled));
            Assert.That(callerCancellation.Token.IsCancellationRequested, Is.True);
        });
    }

    [Test]
    public void AgentCredentialProviderResolvesOnlyOpaqueReferenceAndCancellationToken()
    {
        var reference = new SecretReference(Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();
        var provider = new ContractCredentialProvider("api-key", cancellation.Token);

        var result = provider.ResolveAsync(reference, cancellation.Token).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo("api-key"));
            Assert.That(provider.ObservedReference, Is.SameAs(reference));
            Assert.That(provider.ObservedCancellationToken, Is.EqualTo(cancellation.Token));
        });
    }

    private sealed class ContractCredentialProvider(string value, CancellationToken expectedToken) : IAgentCredentialProvider
    {
        public SecretReference? ObservedReference { get; private set; }
        public CancellationToken ObservedCancellationToken { get; private set; }

        public Task<SecretStoreResult<string>> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            ObservedReference = reference;
            ObservedCancellationToken = cancellationToken;
            if (cancellationToken != expectedToken)
            {
                throw new InvalidOperationException("Cancellation token was not forwarded.");
            }

            return Task.FromResult(SecretStoreResults.Success(value));
        }
    }
}
