using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class SecretServiceCryptographyTests
{
    [Test]
    public void DhSharedSecretIsLeftPaddedBeforeHkdfAndDecryptsIndependentVector()
    {
        // DH private=2 / peer public=8 => shared=64, left padded to 128 bytes.
        // HKDF was calculated independently with node:crypto HMAC-SHA256 (extract, expand 0x01).
        var privateBytes = new byte[] { 2 };
        using var crypto = new SecretServiceCryptography(privateBytes);
        crypto.Establish([8]);
        using var aes = Aes.Create();
        aes.Key = Convert.FromHexString("dd7352d648b81b68e7cfbf88f5c3e2ee");
        var iv = new byte[16];
        var plaintext = Encoding.UTF8.GetBytes("fixture independente");
        using var secret = new SecretServiceSecret("/session", iv, aes.EncryptCbc(plaintext, iv), SecretServiceCryptography.ContentType);
        var decrypted = crypto.Decrypt("/session", secret);
        try
        {
            Assert.That(decrypted, Is.EqualTo(plaintext));
            Assert.That(privateBytes, Is.All.EqualTo((byte)0));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    public void InvalidPeerIsRejectedBeforeDerivation(byte peer)
    {
        using var crypto = new SecretServiceCryptography([2]);
        Assert.Throws<SecretServiceException>(() => crypto.Establish([peer]));
    }

    [Test]
    public void OversizedPeerIsRejected()
    {
        using var crypto = new SecretServiceCryptography([2]);
        Assert.Throws<SecretServiceException>(() => crypto.Establish(new byte[129]));
    }

    [Test]
    public void PeerOutsidePrimeOrderSubgroupIsRejected()
    {
        using var crypto = new SecretServiceCryptography([2]);
        // For the specified group-2 prime, 5^((p-1)/2) mod p = p-1 (not the subgroup identity).
        Assert.Throws<SecretServiceException>(() => crypto.Establish([5]));
    }

    [TestCase(0, 16)]
    [TestCase(15, 16)]
    [TestCase(17, 16)]
    [TestCase(16, 0)]
    [TestCase(16, 15)]
    [TestCase(16, 17)]
    [TestCase(16, 2592)]
    public void MalformedCbcFramingIsRejectedBeforeDecryption(int ivLength, int valueLength)
    {
        using var crypto = new SecretServiceCryptography([2]);
        crypto.Establish([8]);
        using var secret = new SecretServiceSecret("/session", new byte[ivLength], new byte[valueLength], SecretServiceCryptography.ContentType);
        Assert.Throws<SecretServiceException>(() => crypto.Decrypt("/session", secret));
    }

    [Test]
    public void InvalidPkcs7PaddingDoesNotProducePlaintext()
    {
        using var crypto = new SecretServiceCryptography([2]);
        crypto.Establish([8]);
        using var aes = Aes.Create();
        aes.Key = Convert.FromHexString("dd7352d648b81b68e7cfbf88f5c3e2ee");
        var iv = new byte[16];
        // Encrypt a complete zero block without padding; its last byte cannot be valid PKCS7.
        using var secret = new SecretServiceSecret("/session", iv, aes.EncryptCbc(new byte[16], iv, PaddingMode.None), SecretServiceCryptography.ContentType);
        Assert.Throws<CryptographicException>(() => crypto.Decrypt("/session", secret));
    }

    [Test]
    public void SessionMismatchFailsBeforeDecrypting()
    {
        using var crypto = new SecretServiceCryptography([2]);
        crypto.Establish([8]);
        using var encrypted = crypto.Encrypt("/one", [1, 2, 3]);
        Assert.Throws<SecretServiceException>(() => crypto.Decrypt("/two", encrypted));
    }

    [Test]
    public void DisposeClearsPrivateExponentEvenBeforeNegotiation()
    {
        var privateBytes = new byte[] { 2 };
        var crypto = new SecretServiceCryptography(privateBytes);
        crypto.Dispose();
        Assert.That(privateBytes, Is.All.EqualTo((byte)0));
        Assert.Throws<ObjectDisposedException>(() => crypto.Establish([8]));
    }
}
