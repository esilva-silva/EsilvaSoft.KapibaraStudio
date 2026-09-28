using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using Tmds.DBus.Protocol;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LinuxSecretServiceSecretStoreTests
{
    private static readonly SecretReference Reference = new(Guid.Parse("5ce1f37a-ac84-4bd7-aec3-26f36cdcfaf7"), 3);

    [Test]
    public async Task GetDecryptsUtf8AndDisposesItsSessionAndCiphertext()
    {
        var wire = new FakeWire();
        var result = await Store(wire).GetAsync(Reference);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.EqualTo("credencial-á"));
            Assert.That(wire.Disposed, Is.True);
            Assert.That(wire.LastSecret!.Value, Is.All.EqualTo((byte)0));
            Assert.That(wire.Attributes, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["application"] = "EsilvaSoft.SlopStudio", ["scope"] = "agent-provider",
                ["reference"] = Reference.Id.ToString("N"), ["version"] = "3"
            }));
        });
    }

    [Test]
    public async Task SetUpdatesExistingReferenceAndOnlyPassesCiphertextToWire()
    {
        var wire = new FakeWire();
        var result = await Store(wire).SetAsync(Reference, "nova-á");
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(wire.DecryptedWrite, Is.EqualTo("nova-á"));
            Assert.That(wire.CiphertextWrite, Is.Not.EqualTo(Encoding.UTF8.GetBytes("nova-á")));
            Assert.That(wire.LastSecret!.Value, Is.All.EqualTo((byte)0));
            Assert.That(wire.Created, Is.False);
            Assert.That(wire.Disposed, Is.True);
        });
    }

    [Test]
    public async Task SetCreatesInExistingCollectionAfterPromptCompletes()
    {
        var wire = new FakeWire { Missing = true, CreatePrompt = true };
        var result = await Store(wire).SetAsync(Reference, "nova");
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(wire.Created, Is.True);
            Assert.That(wire.PromptCalls, Is.EqualTo(1));
            Assert.That(wire.DecryptedWrite, Is.EqualTo("nova"));
        });
    }

    [Test]
    public async Task SetDoesNotCreateCollectionWhenDefaultIsAbsent()
    {
        var wire = new FakeWire { Missing = true, DefaultCollection = "/" };
        var result = await Store(wire).SetAsync(Reference, "nova");
        Assert.Multiple(() =>
        {
            Assert.That(result.Failure!.Code, Is.EqualTo(SecretStoreFailureCode.NotFound));
            Assert.That(wire.Created, Is.False);
            Assert.That(wire.SessionCalls, Is.Zero);
        });
    }

    [TestCase("get")]
    [TestCase("set")]
    [TestCase("delete")]
    public async Task DismissedPromptNeverReportsSuccess(string operation)
    {
        var wire = new FakeWire { Locked = true, Dismissed = true, DeletePrompt = true };
        var store = Store(wire);
        var failure = operation switch
        {
            "get" => (await store.GetAsync(Reference)).Failure,
            "set" => (await store.SetAsync(Reference, "value")).Failure,
            _ => (await store.DeleteAsync(Reference)).Failure
        };
        Assert.Multiple(() =>
        {
            Assert.That(failure!.Code, Is.EqualTo(SecretStoreFailureCode.Cancelled));
            Assert.That(wire.SessionCalls, Is.Zero);
            Assert.That(wire.Disposed, Is.True);
        });
    }

    [Test]
    public async Task DismissedCreationIsNotSuccessAndClearsCiphertext()
    {
        var wire = new FakeWire { Missing = true, CreatePrompt = true, Dismissed = true };
        var result = await Store(wire).SetAsync(Reference, "canary");
        Assert.Multiple(() =>
        {
            Assert.That(result.Failure!.Code, Is.EqualTo(SecretStoreFailureCode.Cancelled));
            Assert.That(wire.LastSecret!.Value, Is.All.EqualTo((byte)0));
        });
    }

    [Test]
    public async Task RelockAfterPromptReturnsLockedWithoutRepeatingPrompt()
    {
        var wire = new FakeWire { Locked = true, Relock = true };
        var result = await Store(wire).GetAsync(Reference);
        Assert.Multiple(() =>
        {
            Assert.That(result.Failure!.Code, Is.EqualTo(SecretStoreFailureCode.Locked));
            Assert.That(wire.PromptCalls, Is.EqualTo(1));
            Assert.That(wire.SessionCalls, Is.Zero);
        });
    }

    [Test]
    public async Task UnlockPromptAcceptedReadsSecret()
    {
        var wire = new FakeWire { Locked = true };
        var result = await Store(wire).GetAsync(Reference);
        Assert.That(result.Value, Is.EqualTo("credencial-á"));
        Assert.That(wire.PromptCalls, Is.EqualTo(1));
    }

    [TestCase("org.freedesktop.Secret.Error.IsLocked", SecretStoreFailureCode.Locked)]
    [TestCase("org.freedesktop.Secret.Error.NoSuchObject", SecretStoreFailureCode.NotFound)]
    [TestCase("org.freedesktop.DBus.Error.AccessDenied", SecretStoreFailureCode.Denied)]
    [TestCase("org.freedesktop.DBus.Error.ServiceUnknown", SecretStoreFailureCode.Unavailable)]
    [TestCase("org.freedesktop.DBus.Error.InvalidArgs", SecretStoreFailureCode.Corrupt)]
    public async Task RemoteErrorsAreTypedAndNeverExposeServiceMessage(string name, SecretStoreFailureCode expected)
    {
        var wire = new FakeWire { ReadFailure = new DBusErrorReplyException(name, "sensitive-canary") };
        var result = await Store(wire).GetAsync(Reference);
        Assert.Multiple(() =>
        {
            Assert.That(result.Failure!.Code, Is.EqualTo(expected));
            Assert.That(result.Failure.ToString(), Does.Not.Contain("sensitive-canary"));
            Assert.That(wire.Disposed, Is.True);
            Assert.That(wire.PromptCalls, Is.Zero);
        });
    }

    [Test]
    public async Task MultipleMatchingItemsFailClosedBeforeReadingOrDeleting()
    {
        var wire = new FakeWire { Ambiguous = true };
        var result = await Store(wire).DeleteAsync(Reference);
        Assert.That(result.Failure!.Code, Is.EqualTo(SecretStoreFailureCode.Corrupt));
        Assert.That(wire.DeleteCalls, Is.Zero);
    }

    [Test]
    public async Task DeleteMissingReferenceReportsNotFoundWithoutOpeningSession()
    {
        var wire = new FakeWire { Missing = true };
        var result = await Store(wire).DeleteAsync(Reference);
        Assert.That(result.Failure!.Code, Is.EqualTo(SecretStoreFailureCode.NotFound));
        Assert.That(wire.SessionCalls, Is.Zero);
    }

    [Test]
    public async Task AvailabilityDoesNotUnlockOrOpenSession()
    {
        var wire = new FakeWire { Locked = true };
        var result = await Store(wire).GetAvailabilityAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.EqualTo(SecretStoreAvailability.Available));
            Assert.That(wire.SessionCalls, Is.Zero);
            Assert.That(wire.PromptCalls, Is.Zero);
            Assert.That(wire.Attributes, Is.Null);
        });
    }

    [Test]
    public async Task UnsupportedPlatformDoesNotCreateWire()
    {
        var store = new LinuxSecretServiceSecretStore(_ => throw new AssertionException("Wire não deveria ser criado."), () => false, TimeSpan.FromSeconds(1));
        Assert.That((await store.GetAvailabilityAsync()).Value, Is.EqualTo(SecretStoreAvailability.UnsupportedPlatform));
        Assert.That((await store.GetAsync(Reference)).Failure!.Code, Is.EqualTo(SecretStoreFailureCode.Unavailable));
    }

    [Test]
    public async Task MalformedUtf16FailsWithoutExposingEncoderExceptionOrOpeningWire()
    {
        var store = new LinuxSecretServiceSecretStore(_ => throw new AssertionException("Wire não deveria ser criado."), () => true, TimeSpan.FromSeconds(1));
        var result = await store.SetAsync(Reference, "secret-\uD800");
        Assert.That(result.Failure!.Code, Is.EqualTo(SecretStoreFailureCode.Corrupt));
    }

    [Test]
    public void PreCancelledCallerDoesNotCreateWire()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var store = new LinuxSecretServiceSecretStore(_ => throw new AssertionException("Wire não deveria ser criado."), () => true, TimeSpan.FromSeconds(1));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await store.GetAsync(Reference, cts.Token));
    }

    [Test]
    public async Task CallerCancellationDisposesOnlyItsWireAndThrowsCallerToken()
    {
        using var cts = new CancellationTokenSource();
        var waiting = new FakeWire { WaitAtSearch = true };
        var store = Store(waiting);
        var task = store.GetAsync(Reference, cts.Token);
        await waiting.SearchStarted.Task;
        cts.Cancel();
        var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        Assert.That(exception!.CancellationToken, Is.EqualTo(cts.Token));
        Assert.That(waiting.Disposed, Is.True);
        Assert.That((await Store(new FakeWire()).GetAsync(Reference)).IsSuccess, Is.True);
    }

    [Test]
    public async Task DeadlineReturnsUnavailableAndDisposesWire()
    {
        var wire = new FakeWire { WaitAtSearch = true };
        var result = await Store(wire, TimeSpan.FromMilliseconds(20)).GetAsync(Reference);
        Assert.That(result.Failure!.Code, Is.EqualTo(SecretStoreFailureCode.Unavailable));
        Assert.That(wire.Disposed, Is.True);
    }

    [Test]
    public void CancellationAfterWriteDoesNotClaimRollbackAndStillClearsBuffers()
    {
        using var cts = new CancellationTokenSource();
        var wire = new FakeWire { AfterWrite = cts.Cancel };
        Assert.ThrowsAsync<OperationCanceledException>(async () => await Store(wire).SetAsync(Reference, "written", cts.Token));
        Assert.Multiple(() =>
        {
            Assert.That(wire.DecryptedWrite, Is.EqualTo("written"));
            Assert.That(wire.LastSecret!.Value, Is.All.EqualTo((byte)0));
            Assert.That(wire.Disposed, Is.True);
        });
    }

    [Test]
    public async Task InvalidUtf8IsCorruptAndEncryptedBuffersAreCleared()
    {
        var wire = new FakeWire { Plaintext = [0xFF, 0xFE] };
        var result = await Store(wire).GetAsync(Reference);
        Assert.That(result.Failure!.Code, Is.EqualTo(SecretStoreFailureCode.Corrupt));
        Assert.That(wire.LastSecret!.Value, Is.All.EqualTo((byte)0));
    }

    private static LinuxSecretServiceSecretStore Store(FakeWire wire, TimeSpan? timeout = null) =>
        new(token => { wire.Token = token; return wire; }, () => true, timeout ?? TimeSpan.FromSeconds(5));

    private sealed class FakeWire : ISecretServiceWire
    {
        private readonly SecretServiceCryptography _server = new([3]);
        public CancellationToken Token { get; set; }
        public bool Disposed { get; private set; }
        public bool Missing { get; init; }
        public bool Ambiguous { get; init; }
        public bool Locked { get; set; }
        public bool Relock { get; init; }
        public bool Dismissed { get; init; }
        public bool CreatePrompt { get; init; }
        public bool DeletePrompt { get; init; }
        public bool Created { get; private set; }
        public bool WaitAtSearch { get; init; }
        public Exception? ReadFailure { get; init; }
        public Action? AfterWrite { get; init; }
        public byte[] Plaintext { get; init; } = Encoding.UTF8.GetBytes("credencial-á");
        public string DefaultCollection { get; init; } = "/collection";
        public TaskCompletionSource SearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SessionCalls { get; private set; }
        public int PromptCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public IReadOnlyDictionary<string, string>? Attributes { get; private set; }
        public SecretServiceSecret? LastSecret { get; private set; }
        public byte[]? CiphertextWrite { get; private set; }
        public string? DecryptedWrite { get; private set; }

        public Task ConnectAsync() => Task.CompletedTask;
        public Task<string> ReadDefaultCollectionAsync() => Task.FromResult(DefaultCollection);
        public async Task<SecretServiceSearch> SearchAsync(IReadOnlyDictionary<string, string> attributes)
        {
            Attributes = attributes;
            SearchStarted.TrySetResult();
            if (WaitAtSearch)
            {
                await Task.Delay(Timeout.Infinite, Token);
            }

            return new SecretServiceSearch(Missing ? [] : Ambiguous ? ["/item", "/other"] : ["/item"], []);
        }

        public Task<SecretServiceSession> OpenSessionAsync(byte[] publicKey)
        {
            SessionCalls++;
            _server.Establish(publicKey);
            return Task.FromResult(new SecretServiceSession("/session", _server.PublicKey));
        }

        public Task<bool> IsLockedAsync(string path, bool collection) => Task.FromResult(Locked);
        public Task<SecretServiceUnlock> UnlockAsync(string path) => Task.FromResult(new SecretServiceUnlock([], "/prompt"));
        public Task<SecretServicePromptResult> PromptAsync(string path)
        {
            PromptCalls++;
            Locked = Relock;
            return Task.FromResult(new SecretServicePromptResult(Dismissed, "/created", ["/item"]));
        }

        public Task<SecretServiceSecret> GetSecretAsync(string item, string session)
        {
            if (ReadFailure is not null)
            {
                return Task.FromException<SecretServiceSecret>(ReadFailure);
            }

            LastSecret = _server.Encrypt(session, Plaintext);
            return Task.FromResult(LastSecret);
        }

        public Task SetSecretAsync(string item, SecretServiceSecret secret)
        {
            LastSecret = secret;
            CiphertextWrite = (byte[])secret.Value.Clone();
            var plaintext = _server.Decrypt("/session", secret);
            try { DecryptedWrite = Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            AfterWrite?.Invoke();
            return Task.CompletedTask;
        }

        public async Task<SecretServiceCreated> CreateItemAsync(string collection, IReadOnlyDictionary<string, string> attributes, SecretServiceSecret secret)
        {
            Created = true;
            await SetSecretAsync("/created", secret);
            return new SecretServiceCreated(CreatePrompt ? "/" : "/created", CreatePrompt ? "/prompt" : "/");
        }

        public Task<string> DeleteAsync(string item)
        {
            DeleteCalls++;
            return Task.FromResult(DeletePrompt ? "/prompt" : "/");
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _server.Dispose();
            CryptographicOperations.ZeroMemory(Plaintext);
            return ValueTask.CompletedTask;
        }
    }
}
