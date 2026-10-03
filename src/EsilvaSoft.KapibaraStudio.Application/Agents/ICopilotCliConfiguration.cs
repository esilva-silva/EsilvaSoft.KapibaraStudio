namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Shared official CLI selection. Resolving never starts a process or reads credentials.</summary>
public interface ICopilotCliConfiguration
{
    string? ExecutablePath { get; set; }
    string? ValidateExecutablePath(string? path);
    string? ResolveExecutablePath();
}
