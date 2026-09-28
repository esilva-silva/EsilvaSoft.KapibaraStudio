using System.Text;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class WindowsCredentialSecretStoreTests
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidData = 13;
    private const int ErrorNotFound = 1168;
    private const int ErrorNoSuchLogonSession = 1312;

    [Test]
    public async Task SetWritesGenericSecretToVersionedTargetAndClearsManagedUtf8Buffer()
    {
        var native = new FakeCredentialManager();
        var store = new WindowsCredentialSecretStore(native, () => true);
        var reference = new SecretReference(Guid.Parse("4702705f-99fa-4ec0-a5a7-8aec4238b770"), 4);
        const string secret = "ápi-key";

        var result = await store.SetAsync(reference, secret);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(native.LastTarget, Is.EqualTo($"EsilvaSoft.SlopStudio/secret/{reference.Id:N}/v4"));
            Assert.That(native.LastWriteBytes, Is.EqualTo(Encoding.UTF8.GetBytes(secret)));
            Assert.That(native.ObservedWriteBuffer, Is.Not.Null);
            Assert.That(native.ObservedWriteBuffer, Is.All.EqualTo((byte)0));
        });
    }

    [TestCase(ErrorAccessDenied, SecretStoreFailureCode.Denied)]
    [TestCase(ErrorNoSuchLogonSession, SecretStoreFailureCode.Unavailable)]
    [TestCase(ErrorInvalidData, SecretStoreFailureCode.Corrupt)]
    public async Task SetMapsNativeErrorsWithoutReturningProviderText(int nativeError, SecretStoreFailureCode expected)
    {
        var native = new FakeCredentialManager { WriteError = nativeError };
        var store = new WindowsCredentialSecretStore(native, () => true);

        var result = await store.SetAsync(new SecretReference(Guid.NewGuid()), "secret-value");

        Assert.Multiple(() =>
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(expected));
            Assert.That(result.ToString(), Does.Not.Contain("secret-value"));
            Assert.That(native.ObservedWriteBuffer, Is.All.EqualTo((byte)0));
        });
    }

    [Test]
    public async Task SetObservesCancellationAfterNativeWriteAndStillClearsUtf8Buffer()
    {
        using var cancellation = new CancellationTokenSource();
        var native = new FakeCredentialManager { OnWrite = cancellation.Cancel };
        var store = new WindowsCredentialSecretStore(native, () => true);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.SetAsync(new SecretReference(Guid.NewGuid()), "synthetic", cancellation.Token));

        Assert.That(native.ObservedWriteBuffer, Is.All.EqualTo((byte)0));
    }

    [Test]
    public async Task SetRejectsBlobLargerThanWindowsCredentialManagerLimitBeforeNativeCall()
    {
        var native = new FakeCredentialManager();
        var store = new WindowsCredentialSecretStore(native, () => true);

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.SetAsync(new SecretReference(Guid.NewGuid()), new string('x', 2561)));

        Assert.That(native.WriteCalls, Is.Zero);
    }

    [Test]
    public async Task SetRejectsMalformedUtf16WithoutNativeWrite()
    {
        var native = new FakeCredentialManager();
        var store = new WindowsCredentialSecretStore(native, () => true);

        var result = await store.SetAsync(new SecretReference(Guid.NewGuid()), "before\uD800after");

        Assert.Multiple(() =>
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Corrupt));
            Assert.That(native.WriteCalls, Is.Zero);
            Assert.That(native.ObservedWriteBuffer, Is.Null);
        });
    }

    [Test]
    public async Task GetDecodesUtf8AndClearsNativeCopyAfterSuccess()
    {
        var native = new FakeCredentialManager { ReadBytes = Encoding.UTF8.GetBytes("chave-ç") };
        var store = new WindowsCredentialSecretStore(native, () => true);

        var result = await store.GetAsync(new SecretReference(Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.EqualTo("chave-ç"));
            Assert.That(native.LastReadBytes, Is.All.EqualTo((byte)0));
        });
    }

    [Test]
    public async Task GetObservesCancellationAfterNativeReadAndClearsReturnedBytes()
    {
        using var cancellation = new CancellationTokenSource();
        var native = new FakeCredentialManager { ReadBytes = Encoding.UTF8.GetBytes("temporary"), OnRead = cancellation.Cancel };
        var store = new WindowsCredentialSecretStore(native, () => true);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.GetAsync(new SecretReference(Guid.NewGuid()), cancellation.Token));

        Assert.That(native.ReadBytes, Is.All.EqualTo((byte)0));
    }

    [TestCase(ErrorNotFound, SecretStoreFailureCode.NotFound)]
    [TestCase(ErrorAccessDenied, SecretStoreFailureCode.Denied)]
    [TestCase(ErrorNoSuchLogonSession, SecretStoreFailureCode.Unavailable)]
    [TestCase(ErrorInvalidData, SecretStoreFailureCode.Corrupt)]
    public async Task GetMapsNativeErrors(int nativeError, SecretStoreFailureCode expected)
    {
        var native = new FakeCredentialManager { ReadError = nativeError, ReadBytes = Encoding.UTF8.GetBytes("private") };
        var store = new WindowsCredentialSecretStore(native, () => true);

        var result = await store.GetAsync(new SecretReference(Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(expected));
            Assert.That(native.ReadBytes, Is.All.EqualTo((byte)0));
        });
    }

    [Test]
    public async Task InvalidUtf8IsCorruptAndReturnedBytesAreCleared()
    {
        var native = new FakeCredentialManager { ReadBytes = [0xC3, 0x28] };
        var store = new WindowsCredentialSecretStore(native, () => true);

        var result = await store.GetAsync(new SecretReference(Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Corrupt));
            Assert.That(native.ReadBytes, Is.All.EqualTo((byte)0));
        });
    }

    [Test]
    public async Task OversizedNativeBlobIsCorruptAndReturnedBytesAreCleared()
    {
        var native = new FakeCredentialManager { ReadBytes = new byte[2561] };
        var store = new WindowsCredentialSecretStore(native, () => true);

        var result = await store.GetAsync(new SecretReference(Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Corrupt));
            Assert.That(native.ReadBytes, Is.All.EqualTo((byte)0));
        });
    }

    [Test]
    public async Task DeleteMapsNotFoundAndChecksCancellationBeforeNativeCall()
    {
        var native = new FakeCredentialManager { DeleteError = ErrorNotFound };
        var store = new WindowsCredentialSecretStore(native, () => true);
        var missing = await store.DeleteAsync(new SecretReference(Guid.NewGuid()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(missing.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.NotFound));
            Assert.That(async () => await store.DeleteAsync(new SecretReference(Guid.NewGuid()), cancellation.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(native.DeleteCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DeleteObservesCancellationAfterNativeCall()
    {
        using var cancellation = new CancellationTokenSource();
        var native = new FakeCredentialManager { OnDelete = cancellation.Cancel };
        var store = new WindowsCredentialSecretStore(native, () => true);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.DeleteAsync(new SecretReference(Guid.NewGuid()), cancellation.Token));

        Assert.That(native.DeleteCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task CancellationAfterAvailabilityReadIsObservedAndMissingLogonSessionIsUnavailable()
    {
        using var cancellation = new CancellationTokenSource();
        var native = new FakeCredentialManager { ReadError = ErrorNotFound, OnRead = cancellation.Cancel };
        var store = new WindowsCredentialSecretStore(native, () => true);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.GetAvailabilityAsync(cancellation.Token));

        var unavailableNative = new FakeCredentialManager { ReadError = ErrorNoSuchLogonSession };
        var unavailableStore = new WindowsCredentialSecretStore(unavailableNative, () => true);
        var availability = await unavailableStore.GetAvailabilityAsync();

        Assert.That(availability.Value, Is.EqualTo(SecretStoreAvailability.Unavailable));
    }

    [Test]
    public async Task AvailabilityReadFailureMapsAccessDeniedWithoutMutation()
    {
        var native = new FakeCredentialManager { ReadError = ErrorAccessDenied };
        var store = new WindowsCredentialSecretStore(native, () => true);

        var availability = await store.GetAvailabilityAsync();

        Assert.Multiple(() =>
        {
            Assert.That(availability.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Denied));
            Assert.That(native.ReadCalls, Is.EqualTo(1));
            Assert.That(native.WriteCalls, Is.Zero);
            Assert.That(native.DeleteCalls, Is.Zero);
        });
    }

    [Test]
    public async Task AvailableStatusMeansOnlyThatNonceReadReturnedNotFound()
    {
        var native = new FakeCredentialManager { ReadError = ErrorNotFound };
        var store = new WindowsCredentialSecretStore(native, () => true);

        var availability = await store.GetAvailabilityAsync();

        Assert.Multiple(() =>
        {
            Assert.That(availability.Value, Is.EqualTo(SecretStoreAvailability.Available));
            Assert.That(native.WriteCalls, Is.Zero);
            Assert.That(native.DeleteCalls, Is.Zero);
        });
    }

    [Test]
    public async Task AvailabilityDistinguishesUnsupportedPlatformAndDoesNotCallNativeApi()
    {
        var native = new FakeCredentialManager();
        var store = new WindowsCredentialSecretStore(native, () => false);

        var availability = await store.GetAvailabilityAsync();

        Assert.Multiple(() =>
        {
            Assert.That(availability.Value, Is.EqualTo(SecretStoreAvailability.UnsupportedPlatform));
            Assert.That(native.ReadCalls, Is.Zero);
        });
    }

    [Test]
    public async Task WindowsCredentialManagerRoundTripUsesNonceTargetAndCleansItInFinally()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("A prova integrada usa o Windows Credential Manager do usuário atual.");
        }

        var store = new WindowsCredentialSecretStore();
        var availability = await store.GetAvailabilityAsync();
        if (!availability.IsSuccess || availability.Value != SecretStoreAvailability.Available)
        {
            Assert.Ignore("O Credential Manager não está disponível para esta sessão de logon; nenhum target foi criado.");
        }

        var reference = new SecretReference(Guid.NewGuid());
        const string syntheticSecret = "slopstudio-contract-test-only";
        var written = await store.SetAsync(reference, syntheticSecret);
        if (!written.IsSuccess && written.Failure?.Code is SecretStoreFailureCode.Unavailable or SecretStoreFailureCode.Denied)
        {
            Assert.Ignore("A sessão respondeu ao probe de leitura, mas não permite gravar no Credential Manager.");
        }

        Assert.That(written.IsSuccess, Is.True, "Gravação sintética no target nonce falhou.");
        try
        {
            var read = await store.GetAsync(reference);
            Assert.That(read.IsSuccess, Is.True);
            Assert.That(read.Value, Is.EqualTo(syntheticSecret));

            var deleted = await store.DeleteAsync(reference);
            Assert.That(deleted.IsSuccess, Is.True);
        }
        finally
        {
            var cleanup = await store.DeleteAsync(reference);
            Assert.That(cleanup.IsSuccess || cleanup.Failure?.Code == SecretStoreFailureCode.NotFound, Is.True,
                "Não foi possível confirmar a remoção do target sintético.");
        }
    }

    private sealed class FakeCredentialManager : IWindowsCredentialManagerNative
    {
        public int ReadError { get; init; }
        public int WriteError { get; init; }
        public int DeleteError { get; init; }
        public byte[]? ReadBytes { get; init; } = [];
        public Action? OnRead { get; init; }
        public Action? OnWrite { get; init; }
        public Action? OnDelete { get; init; }
        public string? LastTarget { get; private set; }
        public byte[]? LastWriteBytes { get; private set; }
        public byte[]? ObservedWriteBuffer { get; private set; }
        public byte[]? LastReadBytes { get; private set; }
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public int Read(string targetName, out byte[]? secret)
        {
            LastTarget = targetName;
            ReadCalls++;
            OnRead?.Invoke();
            secret = ReadBytes;
            LastReadBytes = secret;
            return ReadError;
        }

        public int Write(string targetName, byte[] secret)
        {
            LastTarget = targetName;
            WriteCalls++;
            ObservedWriteBuffer = secret;
            LastWriteBytes = secret.ToArray();
            OnWrite?.Invoke();
            return WriteError;
        }

        public int Delete(string targetName)
        {
            LastTarget = targetName;
            DeleteCalls++;
            OnDelete?.Invoke();
            return DeleteError;
        }
    }
}
