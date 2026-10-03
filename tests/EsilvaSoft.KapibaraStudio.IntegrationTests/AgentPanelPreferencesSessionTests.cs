using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary><c>WorkspacePreferences.AgentPanel</c>: additive to session version 2 (ADR-056).</summary>
[TestFixture, Category("Integration")]
public sealed class AgentPanelPreferencesSessionTests
{
    private string _directory = null!;
    [Test]
    public async Task CopilotExecutableOverrideRoundTripsAndLegacySessionKeepsAutomaticDiscovery()
    {
        var path = NewDatabasePath();
        var executable = Path.Combine(_directory, "copilot.exe");
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            Assert.That((await repository.LoadSessionAsync()).Preferences.CopilotCliExecutablePath, Is.Null);
            await repository.SaveSessionAsync(new WorkspaceSession { Preferences = new() { CopilotCliExecutablePath = executable } });
        }
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var restored = await repository.LoadSessionAsync();
            Assert.That(restored.Preferences.CopilotCliExecutablePath, Is.EqualTo(executable));
            await repository.SaveSessionAsync(restored with { Preferences = restored.Preferences with { CopilotCliExecutablePath = null } });
        }
        Assert.That(ReadRawSession(path), Does.Not.Contain(nameof(WorkspacePreferences.CopilotCliExecutablePath)));
    }

    [SetUp]
    public void CreateTestDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "kapibara-agent-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }
    [TearDown]
    public void RemoveTestDirectory()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
    [Test]
    public async Task SessionSavedWithoutAgentPanelLoadsNullAndSavesWithoutMaterializingIt()
    {
        var path = NewDatabasePath();
        var original = JsonSerializer.Serialize(new WorkspaceSession { Preferences = new() { Theme = "Escuro" } });
        Assert.That(original, Does.Not.Contain(nameof(WorkspacePreferences.AgentPanel)));
        WriteRawSession(path, original);

        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var loaded = await repository.LoadSessionAsync();
            Assert.That((loaded.Version, loaded.Preferences.AgentPanel, loaded.Preferences.Theme), Is.EqualTo((2, (AgentPanelPreferences?)null, "Escuro")));
            await repository.SaveSessionAsync(loaded with { Preferences = loaded.Preferences with { CodeFontSize = 16 } });
        }

        Assert.That(ReadRawSession(path), Does.Not.Contain(nameof(WorkspacePreferences.AgentPanel)).And.Contain("\"CodeFontSize\":16"));
    }

    [Test]
    public async Task AgentPanelRoundTripsWithModeByNameAndOmitsNullFields()
    {
        var path = NewDatabasePath();
        var conversationId = Guid.NewGuid();
        var panel = new AgentPanelPreferences
        {
            SelectedProviderId = "claude-code",
            SelectedMode = AgentOperationMode.Planning,
            ActiveConversationId = conversationId,
            IsOpen = true,
            PanelWidth = 420,
        };

        using (var repository = new LiteDbConnectionProfileRepository(path))
            await repository.SaveSessionAsync(new WorkspaceSession { Preferences = new() { AgentPanel = panel } });

        var raw = ReadRawSession(path);
        Assert.That(raw, Does.Contain("\"SelectedMode\":\"Planning\"").And.Not.Contain(nameof(AgentPanelPreferences.SelectedModelId)));

        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var loaded = (await repository.LoadSessionAsync()).Preferences.AgentPanel;
            Assert.That(loaded, Is.EqualTo(panel));
        }
    }

    [Test]
    public void InvalidAgentPanelValuesAreRefusedAtTheSessionBoundary()
    {
        var path = NewDatabasePath();
        using var repository = new LiteDbConnectionProfileRepository(path);
        foreach (var panel in new[]
                 {
                     new AgentPanelPreferences { PanelWidth = double.NaN },
                     new AgentPanelPreferences { PanelWidth = -1 },
                     new AgentPanelPreferences { ActiveConversationId = Guid.Empty },
                     new AgentPanelPreferences { SelectedMode = (AgentOperationMode)99 },
                     new AgentPanelPreferences { SelectedProviderId = "claude\ncode" },
                 })
        {
            Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.SaveSessionAsync(new WorkspaceSession { Preferences = new() { AgentPanel = panel } }));
        }
    }

    [Test]
    public void StoredInvalidAgentPanelIsReportedAndThePreviousSessionIsNotOverwritten()
    {
        var path = NewDatabasePath();
        var json = JsonSerializer.Serialize(new WorkspaceSession { Preferences = new() { Theme = "Escuro" } })
            .Replace("\"Theme\":\"Escuro\"", "\"Theme\":\"Escuro\",\"AgentPanel\":{\"PanelWidth\":-5}", StringComparison.Ordinal);
        WriteRawSession(path, json);

        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadSessionAsync());
            Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveSessionAsync(new WorkspaceSession()));
        }

        Assert.That(ReadRawSession(path), Is.EqualTo(json));
    }

    private string NewDatabasePath() => Path.Combine(_directory, "workspace.db");

    private static void WriteRawSession(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = json });
    }

    private static string ReadRawSession(string path)
    {
        using var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        return database.GetCollection("workspaceSession").FindById("current")["json"].AsString;
    }
}
