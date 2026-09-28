using System.ClientModel;
using System.ClientModel.Primitives;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using OpenAI;
using OpenAI.Chat;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;

/// <summary>
/// Baseline OpenAI adapter (lote 7): direct OpenAI API (Chat Completions streaming + function calling, SDK
/// <c>OpenAI</c> 2.14.0) with the user's API Key resolved from the OS vault at the start of each turn. It is not a
/// ChatGPT/Codex subscription login and does not use the experimental Codex App Server. The adapter never executes a
/// tool, never reaches MongoDB and never persists a transcript: it only announces registry tools, translates complete
/// tool calls to <see cref="AgentProviderEvent"/> and forwards the runtime's results back to the model. Data only leaves
/// the machine inside a turn started by the runtime with the context the originating tab captured.
/// </summary>
public sealed class OpenAiAgentProvider : IAgentProvider
{
    public const string Id = "openai";
    public const string DisplayName = "OpenAI API";
    private const int MaxApiKeyChars = 512;

    private readonly IAgentCredentialProvider _credentials;
    private readonly Func<OpenAiAgentProviderOptions> _options;
    private readonly IAgentToolRegistry? _tools;
    private readonly Uri? _endpoint;
    private readonly PipelineTransport? _transport;
    private readonly Lock _gate = new();
    private SecretReference? _rejectedReference;

    public OpenAiAgentProvider(
        IAgentCredentialProvider credentials, OpenAiAgentProviderOptions? options = null, IAgentToolRegistry? tools = null)
        : this(credentials, Fixed(options ?? new OpenAiAgentProviderOptions()), tools)
    {
    }

    /// <param name="credentials">Resolves the API Key only for immediate use by one turn.</param>
    /// <param name="options">Configuration source, read once per status check or session, before any await.</param>
    /// <param name="tools">Shared registry; null disables tool calling.</param>
    public OpenAiAgentProvider(
        IAgentCredentialProvider credentials, Func<OpenAiAgentProviderOptions> options, IAgentToolRegistry? tools)
        : this(credentials, options, tools, null, null)
    {
    }

    /// <summary>Test seam: a fixed endpoint and transport (offline handler). Production always uses the official endpoint.</summary>
    internal OpenAiAgentProvider(
        IAgentCredentialProvider credentials, Func<OpenAiAgentProviderOptions> options, IAgentToolRegistry? tools,
        Uri? endpoint, PipelineTransport? transport)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(options);
        _credentials = credentials;
        _options = options;
        _tools = tools;
        _endpoint = endpoint;
        _transport = transport;
    }

    public string ProviderId => Id;

    /// <summary>External provider: tool output released to it is bound to <c>ProviderExternal("openai")</c>.</summary>
    public bool IsLocal => false;

    internal IAgentToolRegistry? Tools => _tools;

    /// <summary>What the adapter proved with automated contract tests (offline SSE); no real account or model yet.</summary>
    public AgentProviderDescriptor Describe() =>
        new(Id, DisplayName, [AgentAuthenticationMethod.ApiKey], new AgentProviderCapabilities
        {
            Chat = true,
            Streaming = true,
            ToolCalling = true,
            Sessions = true,
            ModelSelection = true,
            UsesNetwork = true,
            Evidence = AgentCapabilityEvidence.AutomatedContract,
        });

    public async Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var availability = await GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        var capabilities = availability.IsAvailable
            ? Describe().Capabilities with { ToolCalling = availability.Capabilities.ToolCalling }
            : AgentProviderCapabilities.None with { UsesNetwork = true };
        return new AgentProviderStatus(availability.IsAvailable, ToAuthState(availability.Reason), capabilities,
            availability.Models, availability.DefaultModel,
            availability.IsAvailable ? null : availability.Reason.ToString());
    }

    /// <summary>
    /// Reads the configuration, the vault slot and the registry catalog; no network call and no data is sent. Tool
    /// calling is declared only when the shared registry currently releases at least one tool with a closed schema.
    /// </summary>
    public async Task<OpenAiAgentAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var options = SnapshotOptions();
        var (_, reason) = await ResolveKeyAsync(options, cancellationToken).ConfigureAwait(false);
        var models = options.AllowedModels.ToArray();
        if (reason != OpenAiAgentUnavailableReason.None)
        {
            return new(false, reason, OpenAiAgentCapabilities.None, models, options.DefaultModel);
        }

        var capabilities = new OpenAiAgentCapabilities(
            OpenAiToolCatalog.Create(_tools).Count > 0, OpenAiCapabilityEvidence.OfflineContract);
        return new(true, OpenAiAgentUnavailableReason.None, capabilities, models, options.DefaultModel);
    }

    public async Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Snapshot of configuration and choice before any await.
        var configured = SnapshotOptions();
        var model = string.IsNullOrEmpty(options.ModelId) ? configured.DefaultModel : options.ModelId;
        if (model is null)
        {
            throw new OpenAiAgentUnavailableException(OpenAiAgentUnavailableReason.ModelNotSelected);
        }

        // Only IDs of the validated configuration (or well-formed ones when no catalog is configured).
        if (!OpenAiAgentProviderOptions.IsValidModelId(model) ||
            (configured.AllowedModels.Count > 0 && !configured.AllowedModels.Contains(model, StringComparer.Ordinal)))
        {
            throw new OpenAiAgentUnavailableException(OpenAiAgentUnavailableReason.ModelNotAllowed);
        }

        var (_, reason) = await ResolveKeyAsync(configured, cancellationToken).ConfigureAwait(false);
        if (reason != OpenAiAgentUnavailableReason.None)
        {
            throw new OpenAiAgentUnavailableException(reason);
        }

        return new OpenAiAgentSession(this, configured, model);
    }

    /// <summary>
    /// Resolves the key for immediate use by one turn. The value is never cached, logged, placed in events or exception
    /// messages; only the typed reason leaves this method on failure. A reference rejected by the service is not used
    /// again in this process until the configuration points to another reference or version.
    /// </summary>
    internal async Task<(string? Key, OpenAiAgentUnavailableReason Reason)> ResolveKeyAsync(
        OpenAiAgentProviderOptions options, CancellationToken cancellationToken)
    {
        var reference = options.CredentialReference;
        lock (_gate)
        {
            if (reference.Equals(_rejectedReference))
            {
                return (null, OpenAiAgentUnavailableReason.CredentialRejected);
            }
        }

        SecretStoreResult<string> result;
        try
        {
            result = await _credentials.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return (null, OpenAiAgentUnavailableReason.VaultUnavailable);
        }

        if (!result.IsSuccess)
        {
            return (null, OpenAiFailureCodes.FromVault(result.Failure!.Code));
        }

        var key = result.Value;
        return IsWellFormedKey(key) ? (key, OpenAiAgentUnavailableReason.None) : (null, OpenAiAgentUnavailableReason.CredentialMalformed);
    }

    /// <summary>HTTP 401: the reference stays rejected in this process, so an invalid or expired key never loops.</summary>
    internal void ReportCredentialRejected(SecretReference reference)
    {
        lock (_gate)
        {
            _rejectedReference = reference;
        }
    }

    internal ChatClient CreateClient(string apiKey, string model, OpenAiAgentProviderOptions configured)
    {
        var options = new OpenAIClientOptions
        {
            // No automatic retry: a rejected key must not loop and a streamed turn must never be replayed.
            RetryPolicy = new ClientRetryPolicy(0),
            NetworkTimeout = configured.NetworkTimeout,
            // Pipeline logging and tracing off: requests carry prompts, context and the Authorization header.
            ClientLoggingOptions = new ClientLoggingOptions
            {
                EnableLogging = false,
                EnableMessageLogging = false,
                EnableMessageContentLogging = false,
            },
            EnableDistributedTracing = false,
        };
        if (_endpoint is not null)
        {
            options.Endpoint = _endpoint;
        }

        if (_transport is not null)
        {
            options.Transport = _transport;
        }

        return new ChatClient(model, new ApiKeyCredential(apiKey), options);
    }

    private OpenAiAgentProviderOptions SnapshotOptions()
    {
        var options = _options() ?? throw new InvalidOperationException("Configuração do provider OpenAI ausente.");
        options.Validate();
        return options;
    }

    private static AgentProviderAuthState ToAuthState(OpenAiAgentUnavailableReason reason) => reason switch
    {
        OpenAiAgentUnavailableReason.None => AgentProviderAuthState.Configured,
        OpenAiAgentUnavailableReason.CredentialNotConfigured => AgentProviderAuthState.NotConfigured,
        OpenAiAgentUnavailableReason.CredentialMalformed or OpenAiAgentUnavailableReason.CredentialRejected =>
            AgentProviderAuthState.Invalid,
        OpenAiAgentUnavailableReason.VaultUnavailable or OpenAiAgentUnavailableReason.VaultLocked or
            OpenAiAgentUnavailableReason.VaultAccessDenied or OpenAiAgentUnavailableReason.VaultPromptCancelled or
            OpenAiAgentUnavailableReason.CredentialCorrupt => AgentProviderAuthState.VaultUnavailable,
        _ => AgentProviderAuthState.Unknown,
    };

    private static Func<OpenAiAgentProviderOptions> Fixed(OpenAiAgentProviderOptions options)
    {
        options.Validate();
        return () => options;
    }

    private static bool IsWellFormedKey(string? key) =>
        key is { Length: > 0 and <= MaxApiKeyChars } && key.All(static c => c is > ' ' and < (char)127);
}
