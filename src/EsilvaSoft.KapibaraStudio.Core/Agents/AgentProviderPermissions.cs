namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>Which data categories may be sent to the provider (the user message is always sent once consent exists).</summary>
public sealed record AgentDataSendingPermissions
{
    /// <summary>Active editor buffer (unsaved text included).</summary>
    public bool ActiveFile { get; init; } = true;

    /// <summary>Files of the workspace folder, attached by the user or read by native tools.</summary>
    public bool WorkspaceFiles { get; init; } = true;

    /// <summary>Files outside the workspace, picked in the native dialog. Off by default.</summary>
    public bool ExternalAttachments { get; init; }

    /// <summary>Connection › database › collection names of the originating tab.</summary>
    public bool TabMetadata { get; init; } = true;

    /// <summary>
    /// Schema inferred by the autocomplete cache (<c>get_cached_schema</c>). Off by default: field names come from
    /// sampled documents and dynamic keys may themselves be data (e.g. e-mails used as keys).
    /// </summary>
    public bool InferredSchema { get; init; }
}

/// <summary>Workspace folder usage and exclusion globs (matched against paths relative to the workspace, with <c>/</c>).</summary>
public sealed record AgentWorkspacePermissions
{
    public static IReadOnlyList<string> DefaultExclusions { get; } = [".env", "*.pem", "*.key", "**/secrets/**"];

    /// <summary>Use the folder opened in the Files panel as the agent workspace.</summary>
    public bool UseFilesFolder { get; init; } = true;

    /// <summary>
    /// Globs excluded from attachments and native reads. A pattern without <c>/</c> matches the file name in any folder;
    /// <c>*</c> never crosses <c>/</c>; <c>**</c> crosses folders; <c>?</c> is one character. Case-insensitive.
    /// </summary>
    public IReadOnlyList<string> Exclusions { get; init; } = DefaultExclusions;

    /// <summary>
    /// Exclusions to enforce: a missing (null) list falls back to <see cref="DefaultExclusions"/>, never to "nothing
    /// excluded". Null items are kept so validation fails closed.
    /// </summary>
    public IReadOnlyList<string> EffectiveExclusions => Exclusions ?? DefaultExclusions;
}

/// <summary>Targets that <c>propose_file_edit</c> may propose changes to (proposals only; nothing is saved to disk).</summary>
public sealed record AgentEditProposalPermissions
{
    public bool ActiveFile { get; init; } = true;

    public bool OtherWorkspaceFiles { get; init; }
}

/// <summary>Chips added automatically when composing a message (the user can still remove them).</summary>
public sealed record AgentAutomaticContextPermissions
{
    public bool ActiveFile { get; init; } = true;

    public bool TabMetadata { get; init; } = true;
}

/// <summary>Which connections the product tools may touch.</summary>
public enum AgentConnectionScope
{
    All = 0,
    Selected = 1,
}

/// <summary>
/// Persistent permissions of one provider, edited in the Permissions window. Versioned and saved with optimistic
/// concurrency by <see cref="Revision"/>. Nothing is sent before <see cref="ExternalDestinationConsentAt"/> is set.
/// MongoDB write tools are unavailable in this version: no field can enable them.
/// </summary>
public sealed record AgentProviderPermissions
{
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Product read tools enabled by default. They expose metadata only (names, indexes, cached schema, workspace
    /// context), never documents; they stay unreachable until consent exists, and <c>get_cached_schema</c> also needs
    /// <see cref="AgentDataSendingPermissions.InferredSchema"/>.
    /// </summary>
    public static IReadOnlyList<string> DefaultEnabledReadTools { get; } =
    [
        "list_connections", "list_databases", "list_collections", "get_indexes", "get_cached_schema",
        "get_workspace_context",
    ];

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required string ProviderId { get; init; }

    /// <summary>0 means never stored.</summary>
    public long Revision { get; init; }

    /// <summary>When the user consented to sending data to the provider's external destination; null blocks every send.</summary>
    public DateTimeOffset? ExternalDestinationConsentAt { get; init; }

    public AgentDataSendingPermissions DataSending { get; init; } = new();

    public AgentWorkspacePermissions Workspace { get; init; } = new();

    /// <summary>Provider-native Read/Glob/Grep inside the workspace folder (subject to the exclusions).</summary>
    public bool NativeFileRead { get; init; } = true;

    /// <summary>Native shell/command tool; every call requires individual approval by default.</summary>
    public bool NativeCommandExecution { get; init; }

    /// <summary>Native file edit/write tools; every call requires individual approval by default.</summary>
    public bool NativeFileWrite { get; init; }

    /// <summary>Native network tools; every call requires individual approval by default.</summary>
    public bool NativeNetwork { get; init; }

    public AgentEditProposalPermissions EditProposals { get; init; } = new();

    public AgentAutomaticContextPermissions AutomaticContext { get; init; } = new();

    public AgentConnectionScope ConnectionScope { get; init; } = AgentConnectionScope.All;

    /// <summary>Used only when <see cref="ConnectionScope"/> is <see cref="AgentConnectionScope.Selected"/>.</summary>
    public IReadOnlyList<Guid> SelectedConnectionIds { get; init; } = [];

    /// <summary>Product read tool names the user enabled; unknown names are ignored by the policy.</summary>
    public IReadOnlyList<string> EnabledReadTools { get; init; } = DefaultEnabledReadTools;

    /// <summary>Categories that ask for confirmation in the Agent and Planning modes (other modes override it).</summary>
    public AgentConfirmationCategories ConfirmationCategories { get; init; } = AgentConfirmationCategories.None;

    /// <summary>Opt-out: when false, conversations are not persisted.</summary>
    public bool KeepHistory { get; init; } = true;

    public AgentOperationMode DefaultMode { get; init; } = AgentOperationMode.Agent;

    public string? DefaultModel { get; init; }

    public bool HasExternalDestinationConsent => ExternalDestinationConsentAt is not null;

    /// <summary>
    /// True when every section is present, every enum value is defined, the format version is supported and the provider
    /// ID is set. Consumers (mode policy, attachment resolver) treat a malformed value as the most restrictive case
    /// (nothing is sent) instead of substituting permissive defaults. A null <see cref="AgentWorkspacePermissions.Exclusions"/>
    /// or <see cref="EnabledReadTools"/> is tolerated: the former means the defaults, the latter no read tool.
    /// </summary>
    public bool IsWellFormed =>
        FormatVersion is >= 1 and <= CurrentFormatVersion &&
        !string.IsNullOrWhiteSpace(ProviderId) &&
        DataSending is not null && Workspace is not null && EditProposals is not null && AutomaticContext is not null &&
        Enum.IsDefined(ConnectionScope) && Enum.IsDefined(DefaultMode) &&
        (ConfirmationCategories & ~AgentConfirmationCategories.All) == 0 &&
        (ConnectionScope != AgentConnectionScope.Selected || SelectedConnectionIds is not null);

    /// <summary>Conservative defaults: no consent (nothing is sent), history kept, reads limited to metadata.</summary>
    public static AgentProviderPermissions Default(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return new AgentProviderPermissions { ProviderId = providerId };
    }
}
