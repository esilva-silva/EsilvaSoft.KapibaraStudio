using EsilvaSoft.SlopStudio.Application;
using Tmds.DBus.Protocol;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Minimal Secret Service binding. It only serializes encrypted secret bytes.</summary>
internal sealed class SecretServiceDbusWire : ISecretServiceWire
{
    private const string ServiceName = "org.freedesktop.secrets";
    private const string Root = "/org/freedesktop/secrets";
    private const string ServiceInterface = "org.freedesktop.Secret.Service";
    private const string CollectionInterface = "org.freedesktop.Secret.Collection";
    private const string ItemInterface = "org.freedesktop.Secret.Item";
    private const string PromptInterface = "org.freedesktop.Secret.Prompt";
    private readonly DBusConnection _connection;
    private readonly CancellationToken _token;
    private readonly CancellationTokenRegistration _registration;
    private string _owner = ServiceName;
    private string? _session;

    public SecretServiceDbusWire(CancellationToken token)
        : this(static () => Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"), token)
    {
    }

    internal SecretServiceDbusWire(Func<string?> readSessionAddress, CancellationToken token)
    {
        var address = ResolveSessionAddress(readSessionAddress, token);
        _connection = new DBusConnection(address);
        _token = token;
        _registration = token.Register(static value => ((DBusConnection)value!).Dispose(), _connection);
    }

    internal static string ResolveSessionAddress(Func<string?> readSessionAddress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Do not use DBusAddress.Session: its fallback can synchronously call XOpenDisplay,
        // bypassing our deadline or loading an unavailable native X11 library. Reading the
        // inherited environment is bounded and performs no display/bus discovery or I/O.
        var address = readSessionAddress();
        token.ThrowIfCancellationRequested();
        if (!SecretServiceSessionAddress.IsValid(address, token))
        {
            // A missing session address is unavailable, even if DISPLAY/XDG_RUNTIME_DIR exists.
            // Never send credentials to a TCP address or enable D-Bus autolaunch fallback.
            throw new SecretServiceException(SecretStoreFailureCode.Unavailable);
        }

        return address;
    }

    public async Task ConnectAsync()
    {
        _token.ThrowIfCancellationRequested();
        await _connection.ConnectAsync().ConfigureAwait(false);
        // A metadata call may activate the installed Secret Service through the existing session bus.
        // It does not start a bus, create a collection, display a prompt or transfer any secret.
        await ReadDefaultCollectionAsync().ConfigureAwait(false);
        _owner = await CallAsync("/org/freedesktop/DBus", "org.freedesktop.DBus", "GetNameOwner", "s",
            (ref MessageWriter writer) => writer.WriteString(ServiceName), ReadString, "org.freedesktop.DBus").ConfigureAwait(false);
        if (!_owner.StartsWith(':'))
        {
            throw new SecretServiceException(SecretStoreFailureCode.Corrupt);
        }
    }

    public Task<string> ReadDefaultCollectionAsync() => CallAsync(Root, ServiceInterface, "ReadAlias", "s",
        (ref MessageWriter writer) => writer.WriteString("default"), ReadPath);

    public Task<SecretServiceSearch> SearchAsync(IReadOnlyDictionary<string, string> attributes) =>
        CallAsync(Root, ServiceInterface, "SearchItems", "a{ss}", (ref MessageWriter writer) => WriteAttributes(ref writer, attributes),
            static (message, _) =>
            {
                var reader = message.GetBodyReader();
                return new SecretServiceSearch(Paths(reader.ReadArrayOfObjectPath()), Paths(reader.ReadArrayOfObjectPath()));
            });

    public async Task<SecretServiceSession> OpenSessionAsync(byte[] publicKey)
    {
        var session = await CallAsync(Root, ServiceInterface, "OpenSession", "sv", (ref MessageWriter writer) =>
        {
            writer.WriteString(SecretServiceCryptography.Algorithm);
            writer.WriteSignature("ay");
            writer.WriteArray(publicKey);
        }, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadSignature("ay");
            var peer = reader.ReadArrayOfByte();
            return new SecretServiceSession(reader.ReadObjectPathAsString(), peer);
        }).ConfigureAwait(false);
        _session = session.Path;
        return session;
    }

    public Task<bool> IsLockedAsync(string path, bool collection) => CallAsync(path, "org.freedesktop.DBus.Properties", "Get", "ss",
        (ref MessageWriter writer) =>
        {
            writer.WriteString(collection ? CollectionInterface : ItemInterface);
            writer.WriteString("Locked");
        }, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadSignature("b");
            return reader.ReadBool();
        });

    public Task<SecretServiceUnlock> UnlockAsync(string path) => CallAsync(Root, ServiceInterface, "Unlock", "ao",
        (ref MessageWriter writer) => writer.WriteArray(new[] { new ObjectPath(path) }), static (message, _) =>
        {
            var reader = message.GetBodyReader();
            return new SecretServiceUnlock(Paths(reader.ReadArrayOfObjectPath()), reader.ReadObjectPathAsString());
        });

    public Task<SecretServicePromptResult> PromptAsync(string path) => SecretServicePrompt.WaitAsync(
        handler => _connection.AddMatchAsync(new MatchRule
        {
            Type = MessageType.Signal, Sender = _owner, Path = path, Interface = PromptInterface, Member = "Completed"
        }, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            var dismissed = reader.ReadBool();
            var result = reader.ReadVariantValue();
            return new SecretServicePromptResult(dismissed,
                result.Type == VariantValueType.ObjectPath ? result.GetObjectPathAsString() : null,
                result.Type == VariantValueType.Array && result.ItemType == VariantValueType.ObjectPath
                    ? Paths(result.GetArray<ObjectPath>()) : null);
        }, notification =>
        {
            if (notification.HasValue)
            {
                handler(notification.Value, null);
            }
            else
            {
                handler(null, new SecretServiceException(SecretStoreFailureCode.Unavailable));
            }
        }, emitOnCapturedContext: false, flags: ObserverFlags.EmitAll).AsTask(),
        () => CallVoidAsync(path, PromptInterface, "Prompt", "s", (ref MessageWriter writer) => writer.WriteString(string.Empty)), _token);

    public Task<SecretServiceSecret> GetSecretAsync(string item, string session) => CallAsync(item, ItemInterface, "GetSecret", "o",
        (ref MessageWriter writer) => writer.WriteObjectPath(session), static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.AlignStruct();
            var sessionPath = reader.ReadObjectPathAsString();
            var parameters = reader.ReadArrayOfByte();
            var value = reader.ReadArrayOfByte();
            return new SecretServiceSecret(sessionPath, parameters, value, reader.ReadString());
        });

    public Task SetSecretAsync(string item, SecretServiceSecret secret) => CallVoidAsync(item, ItemInterface, "SetSecret", "(oayays)",
        (ref MessageWriter writer) => WriteSecret(ref writer, secret));

    public Task<SecretServiceCreated> CreateItemAsync(string collection, IReadOnlyDictionary<string, string> attributes, SecretServiceSecret secret) =>
        CallAsync(collection, CollectionInterface, "CreateItem", "a{sv}(oayays)b", (ref MessageWriter writer) =>
        {
            var dictionary = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString(ItemInterface + ".Label");
            writer.WriteVariant("EsilvaSoft.SlopStudio — credencial de provider");
            writer.WriteDictionaryEntryStart();
            writer.WriteString(ItemInterface + ".Attributes");
            writer.WriteSignature("a{ss}");
            WriteAttributes(ref writer, attributes);
            writer.WriteDictionaryEnd(dictionary);
            WriteSecret(ref writer, secret);
            writer.WriteBool(true);
        }, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            return new SecretServiceCreated(reader.ReadObjectPathAsString(), reader.ReadObjectPathAsString());
        });

    public Task<string> DeleteAsync(string item) => CallAsync(item, ItemInterface, "Delete", null, null, ReadPath);

    public async ValueTask DisposeAsync()
    {
        // Cancellation already disconnected this operation, which invalidates every session/prompt.
        // Normal completion sends Close, with an independent short cleanup deadline.
        try
        {
            if (!_token.IsCancellationRequested && _session is not null && _session != "/")
            {
                using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                using var cleanup = cleanupDeadline.Token.Register(static value => ((DBusConnection)value!).Dispose(), _connection);
                await CallVoidAsync(_session, "org.freedesktop.Secret.Session", "Close", null, null).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is DBusExceptionBase or ObjectDisposedException or OperationCanceledException)
        {
            // Disconnect below is the protocol-defined cleanup fallback, not an operation rollback.
        }
        finally
        {
            _registration.Dispose();
            _connection.Dispose();
        }
    }

    private delegate void WriteBody(ref MessageWriter writer);

    private Task<T> CallAsync<T>(string path, string @interface, string member, string? signature, WriteBody? body,
        MessageValueReader<T> read, string? destination = null)
    {
        _token.ThrowIfCancellationRequested();
        var writer = _connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination: destination ?? _owner, path: path, @interface: @interface, member: member, signature: signature);
            body?.Invoke(ref writer);
            return _connection.CallMethodAsync(writer.CreateMessage(), read);
        }
        finally
        {
            writer.Dispose();
        }
    }

    private Task CallVoidAsync(string path, string @interface, string member, string? signature, WriteBody? body)
    {
        _token.ThrowIfCancellationRequested();
        var writer = _connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination: _owner, path: path, @interface: @interface, member: member, signature: signature);
            body?.Invoke(ref writer);
            return _connection.CallMethodAsync(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static string ReadPath(Message message, object? _) => message.GetBodyReader().ReadObjectPathAsString();
    private static string ReadString(Message message, object? _) => message.GetBodyReader().ReadString();
    private static string[] Paths(ObjectPath[] paths) => Array.ConvertAll(paths, path => path.ToString());

    private static void WriteAttributes(ref MessageWriter writer, IReadOnlyDictionary<string, string> attributes)
    {
        var dictionary = writer.WriteDictionaryStart();
        foreach (var (key, value) in attributes)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(key);
            writer.WriteString(value);
        }

        writer.WriteDictionaryEnd(dictionary);
    }

    private static void WriteSecret(ref MessageWriter writer, SecretServiceSecret secret)
    {
        writer.WriteStructureStart();
        writer.WriteObjectPath(secret.Session);
        writer.WriteArray(secret.Parameters);
        writer.WriteArray(secret.Value);
        writer.WriteString(secret.ContentType);
    }
}

internal static class SecretServicePrompt
{
    public static async Task<SecretServicePromptResult> WaitAsync(
        Func<Action<SecretServicePromptResult?, Exception?>, Task<IDisposable>> observe,
        Func<Task> show, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Keep observer failures as data so a concurrent failing Prompt() cannot leave an unobserved
        // faulted task behind when disposal also completes the observer.
        var completion = new TaskCompletionSource<(SecretServicePromptResult? Result, Exception? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = await observe((result, error) =>
        {
            if (error is not null)
            {
                completion.TrySetResult((null, error));
            }
            else if (result is not null)
            {
                completion.TrySetResult((result, null));
            }
        }).ConfigureAwait(false);
        // AddMatch has completed before Prompt: Completed can arrive before the method reply.
        await show().ConfigureAwait(false);
        var completed = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (completed.Error is not null)
        {
            throw new SecretServiceException(LinuxSecretServiceSecretStore.MapFailure(completed.Error));
        }

        return completed.Result!;
    }
}
