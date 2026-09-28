using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Helpers shared by the result-panel rendering and document-editing tests.</summary>
internal static class ResultPanelTestFixture
{
    public static string Binary(string hex, string subType) =>
        "{\"$binary\":{\"base64\":\"" + Convert.ToBase64String(Convert.FromHexString(hex)) + "\",\"subType\":\"" + subType + "\"}}";

    public static WorkspaceTabViewModel ConsoleTab(WorkspaceTestContext context, ConnectionProfile profile, string text, string database = "loja") =>
        new(context.Workspace) { Profile = profile, Database = database, IsConnected = true, Text = text };

    public static Task<QueryPage> Page(bool truncated, params string[] documents) => Task.FromResult(new QueryPage(documents, TimeSpan.Zero, truncated));
}
