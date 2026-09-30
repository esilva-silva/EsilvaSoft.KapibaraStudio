using EsilvaSoft.KapibaraStudio.Application.Agents;
namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
/// <summary>Resultado do comando oficial e estado filtrado consultado depois.</summary>
public sealed record ClaudeCodeAccountCommandResult(ClaudeCodeAccountCommandState State, ClaudeCodeAuthStatus? AuthStatus);