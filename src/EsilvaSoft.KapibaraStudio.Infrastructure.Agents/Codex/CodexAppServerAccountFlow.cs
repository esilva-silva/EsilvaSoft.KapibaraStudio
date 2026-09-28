using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.CodexAppServer;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Codex;

public enum CodexSubscriptionState { SignedOut, Authenticated, OtherAuthentication, InvalidResponse }

public sealed record CodexSubscriptionStatus(CodexSubscriptionState State, string? PlanType = null);

/// <summary>The validated browser destination is returned only to the UI action that opens it.</summary>
internal sealed record CodexBrowserLogin(Guid LoginId, Uri AuthorizationUrl);

/// <summary>
/// Account-only flow on a dedicated App Server transport. The runtime owns OAuth and keyring storage; this service
/// never reads credentials or forwards raw identity, URL, or server error text to diagnostics.
/// </summary>
internal sealed class CodexAppServerAccountFlow : IAsyncDisposable
{
    private readonly ICodexAppServerConnection _transport;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly Task _notifications;
    private Guid? _activeLogin;
    private TaskCompletionSource<bool>? _completion;
    private (Guid Id, bool Success)? _earlyCompletion;
    private int _disposed;

    private CodexAppServerAccountFlow(ICodexAppServerConnection transport)
    {
        _transport = transport;
        _notifications = ReadNotificationsAsync();
    }

    public static async Task<CodexAppServerAccountFlow> InitializeAsync(ICodexAppServerConnection transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var result = await transport.RequestAsync("initialize", new
        {
            clientInfo = new { name = "kapibarastudio", title = "KapibaraStudio", version = "0.11.0" },
            capabilities = new { experimentalApi = true },
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Codex App Server initialization response is invalid.");
        }

        await transport.NotifyAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
        return new CodexAppServerAccountFlow(transport);
    }

    public static Task<CodexAppServerAccountFlow> InitializeAsync(CodexAppServerJsonRpcTransport transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return InitializeAsync(new CodexAppServerConnection(transport), cancellationToken);
    }

    public async Task<CodexSubscriptionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var result = await _transport.RequestAsync(CodexAccountProtocol.AccountReadMethod,
            new { refreshToken = false }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("requiresOpenaiAuth", out var requiresAuth) ||
            requiresAuth.ValueKind != JsonValueKind.True ||
            !result.TryGetProperty("account", out var account))
        {
            return new CodexSubscriptionStatus(CodexSubscriptionState.InvalidResponse);
        }

        if (account.ValueKind == JsonValueKind.Null)
        {
            return new CodexSubscriptionStatus(CodexSubscriptionState.SignedOut);
        }

        if (account.ValueKind != JsonValueKind.Object ||
            !account.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            return new CodexSubscriptionStatus(CodexSubscriptionState.InvalidResponse);
        }

        if (type.GetString() != "chatgpt")
        {
            return new CodexSubscriptionStatus(CodexSubscriptionState.OtherAuthentication);
        }

        var envelope = JsonSerializer.SerializeToUtf8Bytes(new { id = 0, result });
        return CodexAccountProtocol.TryParseAccountReadResponse(envelope, out var parsed) && parsed is not null
            ? new CodexSubscriptionStatus(CodexSubscriptionState.Authenticated, parsed.PlanType)
            : new CodexSubscriptionStatus(CodexSubscriptionState.InvalidResponse);
    }

    public async Task<CodexBrowserLogin> BeginLoginAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (_activeLogin is not null)
            {
                throw new InvalidOperationException("A Codex login is already pending.");
            }
        }

        var result = await _transport.RequestAsync(CodexAccountProtocol.LoginStartMethod, new
        {
            type = "chatgpt", useHostedLoginSuccessPage = true, appBrand = "codex",
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!TryParseBrowserLogin(result, out var login))
        {
            throw new InvalidDataException("Codex App Server returned an invalid browser login.");
        }

        lock (_gate)
        {
            _activeLogin = login.LoginId;
            _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_earlyCompletion is { } early && early.Id == login.LoginId)
            {
                _completion.TrySetResult(early.Success);
            }

            _earlyCompletion = null;
        }

        return login;
    }

    public async Task<bool> WaitForLoginAsync(Guid loginId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        TaskCompletionSource<bool> completion;
        lock (_gate)
        {
            if (_activeLogin != loginId || _completion is null)
            {
                throw new ArgumentException("Codex login ID is not pending.", nameof(loginId));
            }

            completion = _completion;
        }

        var succeeded = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_activeLogin == loginId)
            {
                _activeLogin = null;
                _completion = null;
            }
        }

        return succeeded && (await GetStatusAsync(cancellationToken).ConfigureAwait(false)).State ==
            CodexSubscriptionState.Authenticated;
    }

    public async Task CancelLoginAsync(Guid loginId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (_activeLogin != loginId)
            {
                throw new ArgumentException("Codex login ID is not pending.", nameof(loginId));
            }
        }

        await _transport.RequestAsync(CodexAccountProtocol.LoginCancelMethod,
            new { loginId = loginId.ToString("D") }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task LogoutAsync(bool confirmed, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!confirmed)
        {
            throw new InvalidOperationException("Codex logout requires explicit confirmation.");
        }

        if ((await GetStatusAsync(cancellationToken).ConfigureAwait(false)).State !=
            CodexSubscriptionState.Authenticated)
        {
            throw new InvalidOperationException("Codex ChatGPT account is not active.");
        }

        var response = await _transport.RequestAsync(CodexAccountProtocol.LogoutMethod,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response.ValueKind != JsonValueKind.Object ||
            (await GetStatusAsync(cancellationToken).ConfigureAwait(false)).State != CodexSubscriptionState.SignedOut)
        {
            throw new InvalidOperationException("Codex logout could not be verified.");
        }
    }

    private static bool TryParseBrowserLogin(JsonElement result, out CodexBrowserLogin login)
    {
        login = null!;
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            type.GetString() != "chatgpt" ||
            !result.TryGetProperty("loginId", out var loginId) || loginId.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(loginId.GetString(), out var id) || id == Guid.Empty ||
            !result.TryGetProperty("authUrl", out var url) || url.ValueKind != JsonValueKind.String ||
            url.GetString() is not { Length: > 0 and <= 4096 } address ||
            !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0 ||
            uri.Host is not ("chatgpt.com" or "auth.openai.com"))
        {
            return false;
        }

        login = new CodexBrowserLogin(id, uri);
        return true;
    }

    private async Task ReadNotificationsAsync()
    {
        try
        {
            await foreach (var message in _transport.Messages.WithCancellation(_stop.Token).ConfigureAwait(false))
            {
                if (message.Id is { } requestId)
                {
                    await _transport.RespondErrorAsync(requestId, -32601, "Unsupported request", _stop.Token)
                        .ConfigureAwait(false);
                    continue;
                }

                if (message.Method != "account/login/completed" || message.Parameters.ValueKind != JsonValueKind.Object ||
                    !message.Parameters.TryGetProperty("loginId", out var loginId) ||
                    loginId.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(loginId.GetString(), out var id) ||
                    !message.Parameters.TryGetProperty("success", out var success) ||
                    success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    continue;
                }

                lock (_gate)
                {
                    if (_activeLogin == id)
                    {
                        _completion?.TrySetResult(success.GetBoolean());
                    }
                    else if (_activeLogin is null)
                    {
                        _earlyCompletion = (id, success.GetBoolean());
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            lock (_gate)
            {
                _completion?.TrySetException(new InvalidOperationException("Codex account notifications stopped."));
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        lock (_gate)
        {
            _completion?.TrySetException(new ObjectDisposedException(nameof(CodexAppServerAccountFlow)));
        }

        await _transport.DisposeAsync().ConfigureAwait(false);
        await _notifications.ConfigureAwait(false);
        _stop.Dispose();
    }
}
