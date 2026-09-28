using System.Numerics;
using System.Security.Cryptography;
using EsilvaSoft.SlopStudio.Application;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Secret Service's specified DH group 2, HKDF-SHA256 and AES128-CBC/PKCS7 session.</summary>
/// <remarks>
/// No downgrade to plain. Explicit byte buffers and AES state are cleared. Like the string-based
/// ISecretStore contract, System.Numerics temporaries cannot promise complete process-memory erasure.
/// This legacy protocol does not provide authenticated encryption or protect against the bus owner.
/// </remarks>
internal sealed class SecretServiceCryptography : IDisposable
{
    internal const string Algorithm = "dh-ietf1024-sha256-aes128-cbc-pkcs7";
    internal const string ContentType = "text/plain; charset=utf-8";
    internal const int MaximumSecretBytes = 2560;
    private static readonly BigInteger Prime = new(Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DDEF9519B3CD" +
        "3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7EDEE386BFB5A899FA5AE9F24117C4B1FE649286651ECE65381FFFFFFFFFFFFFFFF"),
        isUnsigned: true, isBigEndian: true);
    private readonly byte[] _privateKey;
    private byte[]? _key;
    private bool _disposed;

    public SecretServiceCryptography() : this(RandomNumberGenerator.GetBytes(32)) { }

    // Ownership of privateKey transfers to this instance (also allows independent known-answer tests).
    internal SecretServiceCryptography(byte[] privateKey)
    {
        _privateKey = privateKey;
        var exponent = new BigInteger(privateKey, isUnsigned: true, isBigEndian: true);
        if (exponent < 2 || exponent >= Prime - 1)
        {
            CryptographicOperations.ZeroMemory(privateKey);
            throw new SecretServiceException(SecretStoreFailureCode.Corrupt);
        }

        PublicKey = BigInteger.ModPow(2, exponent, Prime).ToByteArray(isUnsigned: true, isBigEndian: true);
    }

    public byte[] PublicKey { get; }

    public void Establish(byte[] peerKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_key is not null || peerKey.Length is 0 or > 128)
        {
            throw new SecretServiceException(SecretStoreFailureCode.Corrupt);
        }

        var peer = new BigInteger(peerKey, isUnsigned: true, isBigEndian: true);
        if (peer < 2 || peer >= Prime - 1 || BigInteger.ModPow(peer, (Prime - 1) / 2, Prime) != BigInteger.One)
        {
            throw new SecretServiceException(SecretStoreFailureCode.Corrupt);
        }

        var shared = BigInteger.ModPow(peer, new BigInteger(_privateKey, isUnsigned: true, isBigEndian: true), Prime)
            .ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[128];
        try
        {
            shared.CopyTo(padded.AsSpan(padded.Length - shared.Length));
            _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, padded, 16);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(_privateKey);
            CryptographicOperations.ZeroMemory(shared);
            CryptographicOperations.ZeroMemory(padded);
        }
    }

    public SecretServiceSecret Encrypt(string session, byte[] plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var aes = Aes.Create();
        aes.Key = _key ?? throw new InvalidOperationException("A sessão segura não foi estabelecida.");
        var iv = RandomNumberGenerator.GetBytes(16);
        return new SecretServiceSecret(session, iv, aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7), ContentType);
    }

    public byte[] Decrypt(string session, SecretServiceSecret secret)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (secret.Session != session || secret.Parameters.Length != 16 || secret.Value.Length is 0 or > MaximumSecretBytes + 16 ||
            secret.Value.Length % 16 != 0 || secret.ContentType != ContentType)
        {
            throw new SecretServiceException(SecretStoreFailureCode.Corrupt);
        }

        using var aes = Aes.Create();
        aes.Key = _key ?? throw new InvalidOperationException("A sessão segura não foi estabelecida.");
        return aes.DecryptCbc(secret.Value, secret.Parameters, PaddingMode.PKCS7);
    }

    public void Dispose()
    {
        _disposed = true;
        CryptographicOperations.ZeroMemory(_privateKey);
        if (_key is not null)
        {
            CryptographicOperations.ZeroMemory(_key);
        }
    }
}
