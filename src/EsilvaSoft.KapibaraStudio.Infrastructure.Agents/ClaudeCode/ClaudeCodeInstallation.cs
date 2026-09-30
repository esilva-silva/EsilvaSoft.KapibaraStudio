using System.Text;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;

public enum ClaudeCodeInstallationState
{
    Found,

    /// <summary>Nenhum executável nativo no caminho configurado, no PATH ou nos locais conhecidos.</summary>
    NotFound,

    /// <summary>Só há shim/script (<c>.cmd</c>, <c>.bat</c>, <c>.ps1</c>, script com shebang) ou arquivo não nativo.</summary>
    UnsupportedExecutable,

    VersionTooLow,
    VersionUnreadable,
    ProbeTimedOut,
    ProbeFailed,
}
/// <summary>Resultado da detecção; o caminho é local do usuário e nunca vai para eventos do chat.</summary>
public sealed record ClaudeCodeInstallation(
    ClaudeCodeInstallationState State,
    string? ExecutablePath = null,
    ClaudeCodeVersion? Version = null)
{
    public bool IsUsable => State == ClaudeCodeInstallationState.Found && ExecutablePath is not null && Version is not null;
}
