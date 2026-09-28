namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record WorkspaceSession
{
    public int Version { get; init; } = 2;
    public WorkspacePreferences Preferences { get; init; } = new();
    public Guid? ActiveTabId { get; init; }
    public string? WorkspaceRootPath { get; init; }
    public string ActiveSidebar { get; init; } = "Connections";
    public WorkspaceDraft[] Tabs { get; init; } = [];
}
