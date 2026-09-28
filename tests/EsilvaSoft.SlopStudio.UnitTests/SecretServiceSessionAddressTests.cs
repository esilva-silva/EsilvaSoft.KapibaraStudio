using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class SecretServiceSessionAddressTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("tcp:host=example.invalid,port=1234")]
    [TestCase("autolaunch:")]
    [TestCase("unix:path=/run/user/1000/bus;tcp:host=example.invalid,port=1234")]
    public void MissingOrNonLocalAddressFailsBeforeNativeDiscoveryOrConnection(string? address)
    {
        var exception = Assert.Throws<SecretServiceException>(() => new SecretServiceDbusWire(() => address, CancellationToken.None));
        Assert.That(exception!.Code, Is.EqualTo(SecretStoreFailureCode.Unavailable));
    }

    [TestCase("unix:path=/run/user/1000/bus")]
    [TestCase("unix:abstract=/tmp/dbus-fixture,guid=1234567890abcdef1234567890abcdef")]
    [TestCase("unix:path=/tmp/session-one;unix:path=/tmp/session-two")]
    [TestCase("unix:path=%2Frun%2Fuser%2F1000%2Fbus")]
    [TestCase("unix:abstract=slop%20session%2C%3B%25")]
    [TestCase("unix:guid=ABCDEF1234567890ABCDEF1234567890,path=/run/user/1000/bus")]
    public void LocalSessionAddressIsUsedWithoutDiscovery(string address)
    {
        Assert.That(SecretServiceDbusWire.ResolveSessionAddress(() => address, CancellationToken.None), Is.EqualTo(address));
    }

    [TestCase("unix:")]
    [TestCase("unix:path=")]
    [TestCase("unix:abstract=")]
    [TestCase("unix:path=/tmp/bus%")]
    [TestCase("unix:path=/tmp/bus%2")]
    [TestCase("unix:path=/tmp/bus%GG")]
    [TestCase("unix:path=/tmp/bus%00")]
    [TestCase("unix:abstract=%00")]
    [TestCase("unix:path=/tmp/bus with space")]
    [TestCase("unix:path=/tmp/bus,unexpected=yes")]
    [TestCase("unix:path=/tmp/bus,path=/tmp/second")]
    [TestCase("unix:path=/tmp/bus,abstract=second")]
    [TestCase("unix:path=/tmp/bus,guid=abcd")]
    [TestCase("unix:path=/tmp/bus,guid=0123456789abcdef0123456789abcdeg")]
    [TestCase("unix:path=/tmp/bus,guid=0123456789abcdef0123456789abcdef00")]
    [TestCase("unix:path=/tmp/bus,guid=")]
    [TestCase("unix:guid=0123456789abcdef0123456789abcdef")]
    [TestCase("unix:path=/tmp/bus,guid=0123456789abcdef0123456789abcdef,guid=0123456789abcdef0123456789abcdef")]
    [TestCase("unix:path=/tmp/bus,noncefile=/tmp/nonce")]
    [TestCase("unix:tmpdir=/tmp")]
    [TestCase("unix:runtime=yes")]
    [TestCase("unix:path=/tmp/bus;")]
    [TestCase(";unix:path=/tmp/bus")]
    [TestCase("unix:path=/tmp/bus,,guid=0123456789abcdef0123456789abcdef")]
    public void InvalidUnixGrammarIsUnavailableBeforeConnection(string address)
    {
        var exception = Assert.Throws<SecretServiceException>(() => new SecretServiceDbusWire(() => address, CancellationToken.None));
        Assert.That(exception!.Code, Is.EqualTo(SecretStoreFailureCode.Unavailable));
    }

    [Test]
    public void TooManyAlternativesAreRejectedButTheLimitIsAccepted()
    {
        var maximum = string.Join(';', Enumerable.Repeat("unix:path=/run/user/1000/bus", SecretServiceSessionAddress.MaximumAlternatives));
        Assert.That(SecretServiceDbusWire.ResolveSessionAddress(() => maximum, CancellationToken.None), Is.EqualTo(maximum));
        Assert.Throws<SecretServiceException>(() => SecretServiceDbusWire.ResolveSessionAddress(() => maximum + ";unix:path=/tmp/bus", CancellationToken.None));
    }

    [Test]
    public void OversizedAddressIsRejectedBeforeParsing()
    {
        var address = "unix:path=/" + new string('x', SecretServiceSessionAddress.MaximumAddressCharacters);
        Assert.Throws<SecretServiceException>(() => SecretServiceDbusWire.ResolveSessionAddress(() => address, CancellationToken.None));
    }

    [Test]
    public void SocketNameBoundUsesDecodedBytes()
    {
        var maximum = "unix:abstract=" + string.Concat(Enumerable.Repeat("%61", SecretServiceSessionAddress.MaximumSocketNameBytes));
        Assert.That(SecretServiceDbusWire.ResolveSessionAddress(() => maximum, CancellationToken.None), Is.EqualTo(maximum));
        Assert.Throws<SecretServiceException>(() => SecretServiceDbusWire.ResolveSessionAddress(() => maximum + "%61", CancellationToken.None));
    }

    [Test]
    public void PreCancelledOperationDoesNotEvenReadEnvironment()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var exception = Assert.Throws<OperationCanceledException>(() => new SecretServiceDbusWire(
            () => throw new AssertionException("A resolução não deveria executar."), cts.Token));
        Assert.That(exception!.CancellationToken, Is.EqualTo(cts.Token));
    }

    [Test]
    public void CancellationDuringResolutionPreventsConnectionConstruction()
    {
        using var cts = new CancellationTokenSource();
        var exception = Assert.Throws<OperationCanceledException>(() => new SecretServiceDbusWire(() =>
        {
            cts.Cancel();
            return "unix:path=/run/user/1000/bus";
        }, cts.Token));
        Assert.That(exception!.CancellationToken, Is.EqualTo(cts.Token));
    }
}
