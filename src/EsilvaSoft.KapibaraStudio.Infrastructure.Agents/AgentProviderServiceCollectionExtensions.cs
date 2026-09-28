using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Anthropic;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Codex;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.OpenAi;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents;

public static class AgentProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenAI API adapter as an <see cref="IAgentProvider"/>. Registration opens no connection and reads
    /// no secret: the provider only reports itself available, and only creates sessions, when an API Key exists in the
    /// vault slot. <c>AddKapibaraStudioInfrastructure</c> always composes the shared <see cref="IAgentToolRegistry"/> (the
    /// MCP broker only controls whether an external client can reach it, never whether it exists); when this call is
    /// composed alongside it, the registry is used, otherwise the provider declares no tool calling. Requires
    /// <see cref="IAgentCredentialProvider"/>.
    /// </summary>
    public static IServiceCollection AddKapibaraStudioOpenAiAgentProvider(
        this IServiceCollection services, OpenAiAgentProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(OpenAiAgentProvider)))
        {
            throw new InvalidOperationException("O provider OpenAI já foi composto.");
        }

        var configured = options ?? new OpenAiAgentProviderOptions();
        configured.Validate();
        EnsureToolResultWaitCovers(configured.ToolResultTimeout, RuntimeOptionsOf(services), "OpenAI");
        services.AddSingleton(provider =>
        {
            EnsureToolResultWaitCovers(configured.ToolResultTimeout, RuntimeOptionsOf(provider), "OpenAI");
            return new OpenAiAgentProvider(
                provider.GetRequiredService<IAgentCredentialProvider>(), configured, provider.GetService<IAgentToolRegistry>());
        });
        services.AddSingleton<IAgentProvider>(provider => provider.GetRequiredService<OpenAiAgentProvider>());
        return services;
    }

    /// <summary>
    /// Registers the Claude API adapter as an <see cref="IAgentProvider"/>. Registration opens no connection and reads
    /// no secret: the provider only reports itself available, and only creates sessions, when an API Key exists in the
    /// vault slot. Authentication is API Key only, resolved by <see cref="IAgentCredentialProvider"/> from the OS
    /// vault at the start of each turn; it never imports a Claude Desktop/Code session or offers a subscription login.
    /// <c>AddKapibaraStudioInfrastructure</c> always composes the shared <see cref="IAgentToolRegistry"/> (the MCP broker
    /// only controls whether an external client can reach it, never whether it exists); when this call is composed
    /// alongside it, the registry is used, otherwise the provider declares no tool calling. The registered instance
    /// owns its HTTP handler and is disposed with the container.
    /// </summary>
    public static IServiceCollection AddKapibaraStudioClaudeAgentProvider(
        this IServiceCollection services, ClaudeAgentProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(ClaudeAgentProvider)))
        {
            throw new InvalidOperationException("O provider Claude já foi composto.");
        }

        var configured = options ?? new ClaudeAgentProviderOptions();
        configured.Validate();
        EnsureToolResultWaitCovers(configured.Budget.ToolResultTimeout, RuntimeOptionsOf(services), "Claude");
        services.AddSingleton(provider =>
        {
            EnsureToolResultWaitCovers(configured.Budget.ToolResultTimeout, RuntimeOptionsOf(provider), "Claude");
            return new ClaudeAgentProvider(
                provider.GetRequiredService<IAgentCredentialProvider>(), configured, provider.GetService<IAgentToolRegistry>());
        });
        services.AddSingleton<IAgentProvider>(provider => provider.GetRequiredService<ClaudeAgentProvider>());
        return services;
    }

    /// <summary>
    /// Registers the "Claude (assinatura)" mode (ADR-053): the user's own Claude Code binary run as a subprocess, separate
    /// from the Claude API provider (different ID, descriptor and sessions; never a fallback of one another). Registration
    /// is lazy and side-effect free: no process, file or network access happens until the user explicitly checks the
    /// status or opens a session. Authentication belongs entirely to the official CLI; no credential is resolved here.
    /// </summary>
    /// <remarks>
    /// <c>mcpChannel</c> (optional, P7-CLP-4) resolves the per-session MCP channel provisioner that carries the product
    /// tools. Null keeps the provider without product tools (a plan that needs them fails with a typed error).
    /// </remarks>
    public static IServiceCollection AddKapibaraStudioClaudeCodeAgentProvider(
        this IServiceCollection services, ClaudeCodeAgentProviderOptions? options = null,
        Func<IServiceProvider, IAgentMcpChannelProvisioner?>? mcpChannel = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(ClaudeCodeAgentProvider)))
        {
            throw new InvalidOperationException("O provider Claude (assinatura) já foi composto.");
        }

        var configured = options ?? new ClaudeCodeAgentProviderOptions();
        configured.Validate();
        services.AddSingleton(provider => new ClaudeCodeAgentProvider(configured, mcpChannel?.Invoke(provider)));
        services.AddSingleton<IAgentProvider>(provider => provider.GetRequiredService<ClaudeCodeAgentProvider>());
        return services;
    }

    /// <summary>Registers the experimental ChatGPT subscription adapter over the official Codex App Server.</summary>
    public static IServiceCollection AddKapibaraStudioCodexSubscriptionAgentProvider(
        this IServiceCollection services, CodexSubscriptionAgentProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(CodexSubscriptionAgentProvider)))
        {
            throw new InvalidOperationException("O provider Codex por assinatura já foi composto.");
        }

        services.AddSingleton(provider => new CodexSubscriptionAgentProvider(options,
            provider.GetService<IAgentToolRegistry>()));
        services.AddSingleton<IAgentProvider>(provider => provider.GetRequiredService<CodexSubscriptionAgentProvider>());
        return services;
    }

    /// <summary>
    /// Margin above <see cref="AgentRuntimeOptions.MaxToolCallDuration"/> for delivering the result to the adapter,
    /// scheduling and the cooperative return of a cancelled call (including the registry's bounded closing of a
    /// cancelled write intent, at most 5 s + 5 s).
    /// </summary>
    public static readonly TimeSpan ToolResultWaitMargin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The adapter's wait for tool results must outlast the runtime's worst-case tool call (a write waiting for human
    /// approval), or the model would see a timeout while the approval is still open and the runtime could still apply
    /// the write. Checked at registration against the runtime options already composed and again at resolution, so
    /// the registration order does not matter; without a composed runtime the defaults apply.
    /// </summary>
    /// <exception cref="InvalidOperationException">The wait does not cover the runtime budget plus the margin.</exception>
    internal static void EnsureToolResultWaitCovers(TimeSpan toolResultTimeout, AgentRuntimeOptions runtime, string provider)
    {
        var required = runtime.MaxToolCallDuration + ToolResultWaitMargin;
        if (toolResultTimeout < required)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"A espera por resultados de tool do provider {provider} ({toolResultTimeout.TotalSeconds:0} s) é menor que o pior caso de uma chamada do runtime com folga ({required.TotalSeconds:0} s)."));
        }
    }

    /// <summary>
    /// The runtime budget composed by <c>AddKapibaraStudioInfrastructure</c> (the same <c>AgentPlatformOptions.Runtime</c>
    /// instance the runtime host uses, registered exactly once). A second registration would make the budget the
    /// runtime enforces ambiguous, so it is refused instead of picking the last one.
    /// </summary>
    private static AgentRuntimeOptions RuntimeOptionsOf(IServiceCollection services)
    {
        var registered = services.Where(static descriptor => descriptor.ServiceType == typeof(AgentRuntimeOptions)).ToArray();
        return registered.Length switch
        {
            0 => AgentRuntimeOptions.Default,
            1 when registered[0].ImplementationInstance is AgentRuntimeOptions options => options,
            _ => throw new InvalidOperationException("As opções do runtime de agentes precisam ser uma única instância composta."),
        };
    }

    private static AgentRuntimeOptions RuntimeOptionsOf(IServiceProvider provider)
    {
        var registered = provider.GetServices<AgentRuntimeOptions>().ToArray();
        return registered.Length switch
        {
            0 => AgentRuntimeOptions.Default,
            1 => registered[0],
            _ => throw new InvalidOperationException("As opções do runtime de agentes precisam ser uma única instância composta."),
        };
    }
}
