using System.Text.RegularExpressions;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;

/// <summary>
/// Configuration and budgets of the OpenAI API adapter (lote 7). Defaults are conservative product limits, not
/// measured guarantees; they bound one turn in tokens, characters (bytes sent/received), rounds and time. No
/// commercial model is fixed here: the model comes from the session or from explicit configuration.
/// </summary>
public sealed partial record OpenAiAgentProviderOptions
{
    /// <summary>
    /// Opaque vault slot of the user's OpenAI API key. The key itself only lives in the OS secret store; the slot
    /// contains no credential material. Per-account references persisted by the LiteDB owner are a pending contract.
    /// </summary>
    public static SecretReference DefaultCredentialReference { get; } =
        new(Guid.ParseExact("5b0f6f3e8f7a4c1b9d2e7a3c6b1d4e90", "N"));

    public SecretReference CredentialReference { get; init; } = DefaultCredentialReference;

    /// <summary>Model used when the session does not choose one. Null requires an explicit choice per session.</summary>
    public string? DefaultModel { get; init; }

    /// <summary>When not empty, only these model IDs may be used (configuration catalog).</summary>
    public IReadOnlyList<string> AllowedModels { get; init; } = [];

    /// <summary>Server-side output cap of each request (<c>max_completion_tokens</c>).</summary>
    public int MaxOutputTokensPerRequest { get; init; } = 4096;

    /// <summary>Total tokens reported by the service for one turn, summed across rounds.</summary>
    public long MaxTokensPerTurn { get; init; } = 64_000;

    /// <summary>Requests per turn (first request plus continuations after tool results).</summary>
    public int MaxModelRoundsPerTurn { get; init; } = 8;

    public int MaxToolCallsPerRound { get; init; } = 8;

    public int MaxToolArgumentsChars { get; init; } = 64 * 1024;

    /// <summary>Largest tool result forwarded to the provider; larger results are replaced by a failure code.</summary>
    public int MaxToolResultChars { get; init; } = 256 * 1024;

    public int MaxUserMessageChars { get; init; } = 32 * 1024;

    public int MaxAuthorizedContextChars { get; init; } = 64 * 1024;

    /// <summary>Upper bound of characters sent in one request (system, history, context, tools results).</summary>
    public int MaxRequestChars { get; init; } = 512 * 1024;

    /// <summary>Characters received from the model in one turn (text and tool arguments).</summary>
    public int MaxResponseCharsPerTurn { get; init; } = 256 * 1024;

    /// <summary>Completed exchanges kept in memory for the logical session. Nothing is persisted.</summary>
    public int MaxHistoryTurns { get; init; } = 16;

    /// <summary>Timeout of one network operation (connect or one stream read).</summary>
    public TimeSpan NetworkTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Deadline of one streamed request, from send to the terminal chunk.</summary>
    public TimeSpan RoundTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long a turn waits for the runtime to answer the tool calls of one round. Must cover
    /// <c>AgentRuntimeOptions.MaxToolCallDuration</c> (200 s by default, a write waiting for human approval) plus a
    /// 30 s margin; the composition refuses less. <see cref="RoundTimeout"/> bounds only the streamed request.
    /// </summary>
    public TimeSpan ToolResultTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Bound to confirm that the turn producer stopped after cancellation or disposal.</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);

    internal static bool IsValidModelId(string? model) => model is not null && ModelIdPattern().IsMatch(model);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(CredentialReference);
        ArgumentNullException.ThrowIfNull(AllowedModels);
        if ((DefaultModel is not null && !IsValidModelId(DefaultModel)) || AllowedModels.Any(model => !IsValidModelId(model)) ||
            (DefaultModel is not null && AllowedModels.Count > 0 && !AllowedModels.Contains(DefaultModel, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Configuração de modelo OpenAI inválida.", nameof(DefaultModel));
        }

        if (MaxOutputTokensPerRequest is < 16 or > 1_000_000 || MaxTokensPerTurn < MaxOutputTokensPerRequest ||
            MaxModelRoundsPerTurn is < 1 or > 64 || MaxToolCallsPerRound is < 1 or > 64 || MaxToolArgumentsChars < 2 ||
            MaxToolResultChars < 64 || MaxUserMessageChars < 1 || MaxAuthorizedContextChars < 0 ||
            MaxRequestChars < MaxUserMessageChars + MaxAuthorizedContextChars || MaxResponseCharsPerTurn < 1 ||
            MaxHistoryTurns < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OpenAiAgentProviderOptions), "Limites do provider OpenAI inválidos.");
        }

        foreach (var value in new[] { NetworkTimeout, RoundTimeout, ToolResultTimeout, StopTimeout })
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromHours(1))
            {
                throw new ArgumentOutOfRangeException(nameof(OpenAiAgentProviderOptions), "Prazos do provider OpenAI inválidos.");
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelIdPattern();
}
