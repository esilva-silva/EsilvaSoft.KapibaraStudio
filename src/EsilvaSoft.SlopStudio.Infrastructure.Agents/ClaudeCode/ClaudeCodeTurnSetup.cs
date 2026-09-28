using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Tradução literal de um <see cref="AgentTurnPlan"/> para o Claude Code, sem decidir política: o que entra em
/// <c>--tools</c>, <c>--settings</c> (<c>deny</c>/<c>ask</c>/<c>allow</c>), <c>--mcp-config</c> e
/// <c>--permission-prompt-tool</c>, e o que o <c>system/init</c> e os <c>tool_use</c> podem conter. O plano vem sempre da
/// <c>AgentModePolicy</c>; aqui ele só pode ser recusado, nunca alargado.
/// </summary>
internal sealed record ClaudeCodeTurnSetup
{
    private ClaudeCodeTurnSetup(
        IReadOnlyList<string> nativeTools, IReadOnlyList<string> productTools, bool requiresPermissionPromptTool,
        string settingsJson, IReadOnlyList<string> askRules, IReadOnlyList<string> allowRules)
    {
        NativeTools = nativeTools;
        ProductTools = productTools;
        RequiresPermissionPromptTool = requiresPermissionPromptTool;
        SettingsJson = settingsJson;
        AskRules = askRules;
        AllowRules = allowRules;
    }

    public IReadOnlyList<string> NativeTools { get; }

    /// <summary>Nomes do registry (sem prefixo MCP) expostos pelo canal da sessão.</summary>
    public IReadOnlyList<string> ProductTools { get; }

    public bool RequiresPermissionPromptTool { get; }

    /// <summary>O turno precisa do servidor MCP do produto: há tools do produto ou a ferramenta de aprovação.</summary>
    public bool RequiresMcpChannel => ProductTools.Count > 0 || RequiresPermissionPromptTool;

    public string SettingsJson { get; }

    public IReadOnlyList<string> AskRules { get; }

    public IReadOnlyList<string> AllowRules { get; }

    /// <summary>JSON inline de <c>--mcp-config</c>; nulo até o canal estar pronto (e sempre nulo sem canal).</summary>
    public string? McpConfigJson { get; private init; }

    public string? PermissionPromptTool => RequiresPermissionPromptTool ? ClaudeCodeCommandLine.PermissionPromptToolName : null;

    /// <summary>
    /// Ferramentas do modelo esperadas em <c>init.tools</c>: nativas ∪ MCP do produto. A ferramenta usada internamente
    /// pelo permission prompt e EndConversation podem não ser anunciadas nesse inventário.
    /// </summary>
    public IReadOnlySet<string> ExpectedInitTools
    {
        get
        {
            var tools = new HashSet<string>(NativeTools, StringComparer.Ordinal);
            foreach (var tool in ProductTools)
            {
                tools.Add(McpServerLaunchSpec.ToolName(tool));
            }

            return tools;
        }
    }

    public IReadOnlySet<string> OptionalInitTools => RequiresMcpChannel
        ? new HashSet<string>(RequiresPermissionPromptTool
            ? [ClaudeCodeCommandLine.PermissionPromptToolName, "EndConversation"]
            : ["EndConversation"], StringComparer.Ordinal)
        : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Nome no <c>tool_use</c> → nome publicado no evento. A ferramenta de aprovação não está aqui de propósito: só a CLI a
    /// chama (permission prompt), nunca o modelo; um <c>tool_use</c> dela aborta o turno.
    /// </summary>
    public IReadOnlyDictionary<string, string> CallableTools
    {
        get
        {
            var tools = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tool in NativeTools)
            {
                tools[tool] = tool;
            }

            foreach (var tool in ProductTools)
            {
                tools[McpServerLaunchSpec.ToolName(tool)] = tool;
            }

            return tools;
        }
    }

    public ClaudeCodeTurnSetup WithMcpServer(McpServerLaunchSpec launch) =>
        this with { McpConfigJson = ClaudeCodeCommandLine.BuildMcpConfigJson(launch) };

    /// <summary>
    /// Traduz o plano ou devolve o código tipado de recusa. Recusa: plano ausente/bloqueado, ferramenta nativa fora da
    /// allowlist do adapter, tool do produto desconhecida (inclui escrita e <c>approve</c>), regra com caractere de
    /// controle ou grande demais, confirmação exigida sem a ferramenta de aprovação, e tools do produto sem permissões.
    /// </summary>
    public static bool TryCreate(
        AgentTurnPlan? plan, AgentProviderPermissions? permissions, ClaudeCodeLaunchProfile profile,
        out ClaudeCodeTurnSetup? setup, out string? errorCode)
    {
        setup = null;
        if (plan is null)
        {
            errorCode = ClaudeCodeErrorCodes.TurnPlanMissing;
            return false;
        }

        if (plan.IsBlocked)
        {
            errorCode = ClaudeCodeErrorCodes.TurnBlocked;
            return false;
        }

        errorCode = ClaudeCodeErrorCodes.TurnPlanInvalid;
        if (plan.NativeTools is null || plan.ProductTools is null || plan.NativeAskRules is null || plan.NativeDenyRules is null ||
            !Enum.IsDefined(plan.ProposalHandling))
        {
            return false;
        }

        var native = plan.NativeTools.ToArray();
        if (native.Distinct(StringComparer.Ordinal).Count() != native.Length ||
            !native.All(static tool => ClaudeCodeAgentProviderOptions.NativeToolAllowlist.Contains(tool, StringComparer.Ordinal)))
        {
            return false;
        }

        // The provider adapter is a second fail-closed boundary: a stale/tampered plan may not expose a CLI tool just
        // because it is part of the adapter's maximum allowlist. Every modifying/network tool must also be gated by
        // its persisted permission and routed through the official per-call approval prompt.
        foreach (var tool in native)
        {
            var permission = tool switch
            {
                // Older internal callers may send the already-narrowed, trusted read plan without permissions; an
                // explicit saved opt-out still wins. Risk-bearing tools always require an affirmative persisted field.
                "Read" or "Glob" or "Grep" => permissions?.NativeFileRead != false,
                "Bash" => permissions?.NativeCommandExecution == true,
                "Edit" or "Write" => permissions?.NativeFileWrite == true,
                "WebSearch" or "WebFetch" => permissions?.NativeNetwork == true,
                _ => false,
            };
            var category = tool switch
            {
                "Read" or "Glob" or "Grep" => AgentConfirmationCategories.NativeFileRead,
                "Bash" => AgentConfirmationCategories.NativeCommand,
                "Edit" or "Write" => AgentConfirmationCategories.NativeFileWrite,
                "WebSearch" or "WebFetch" => AgentConfirmationCategories.NativeNetwork,
                _ => AgentConfirmationCategories.None,
            };
            var requiresPerCallApproval = category is AgentConfirmationCategories.NativeCommand or
                AgentConfirmationCategories.NativeFileWrite or AgentConfirmationCategories.NativeNetwork;
            if (!permission || (requiresPerCallApproval &&
                (!plan.RequiresPermissionPromptTool || !plan.NativeAskRules.Contains(tool, StringComparer.Ordinal))))
            {
                return false;
            }
        }

        var product = plan.ProductTools.ToArray();
        if (product.Distinct(StringComparer.Ordinal).Count() != product.Length ||
            !product.All(static tool => IsProductTool(tool)))
        {
            return false;
        }

        if (!plan.NativeAskRules.All(IsSafeRule) || !plan.NativeDenyRules.All(IsSafeRule))
        {
            return false;
        }

        var ask = new List<string>(plan.NativeAskRules);
        var allow = new List<string>();
        foreach (var tool in product)
        {
            var name = McpServerLaunchSpec.ToolName(tool);
            if ((plan.ConfirmationCategories & AgentProductToolNames.CategoryOf(tool)) != 0)
            {
                // Confirmação por chamada: explicitamente ask (prevalece sobre allow do usuário) e roteada à aprovação.
                if (!plan.RequiresPermissionPromptTool)
                {
                    return false;
                }

                ask.Add(name);
            }
            else
            {
                // Sem confirmação no plano: em -p/default a CLI a executaria só com prompt, que não existe; allow explícito.
                allow.Add(name);
            }
        }

        if (profile.WorkingDirectoryKind == ClaudeCodeWorkingDirectoryKind.Dedicated)
        {
            // Pasta dedicada vazia (comportamento anterior preservado): toda leitura nativa é ask.
            ask.AddRange(native);
        }

        if ((product.Length > 0 || plan.RequiresPermissionPromptTool) && permissions is null)
        {
            return false;
        }

        var deny = profile.FixedDenyRules.Concat(plan.NativeDenyRules).ToArray();
        setup = new ClaudeCodeTurnSetup(native, product, plan.RequiresPermissionPromptTool,
            ClaudeCodeCommandLine.BuildSettingsJson(deny, ask, allow), ask, allow);
        errorCode = null;
        return true;
    }

    private static bool IsProductTool(string? tool) =>
        tool is { Length: > 0 and <= 64 } && tool.All(static c => c is (>= 'a' and <= 'z') or '_') &&
        !AgentProductToolNames.NativeFileReadTools.Contains(tool, StringComparer.Ordinal) &&
        AgentProductToolNames.CategoryOf(tool) != AgentConfirmationCategories.None;

    private static bool IsSafeRule(string? rule) =>
        rule is { Length: > 0 and <= 512 } && !rule.Any(char.IsControl) && !char.IsWhiteSpace(rule[0]);
}
