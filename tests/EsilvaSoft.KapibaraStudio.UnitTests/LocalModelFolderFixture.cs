using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Synthetic on-disk model folders shared by the local-model catalog and preferences tests.</summary>
internal static class LocalModelFolderFixture
{
    public static string CreateQwenModel(string directory, string folder, string? metadata = null)
    {
        var root = Path.Combine(directory, folder);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "genai_config.json"), "{\"model\":{\"type\":\"qwen2\",\"context_length\":4096,\"decoder\":{\"filename\":\"model.onnx\"}}}");
        File.WriteAllText(Path.Combine(root, "model.onnx"), "synthetic");
        File.WriteAllText(Path.Combine(root, "tokenizer_config.json"), "{}");
        File.WriteAllText(Path.Combine(root, "tokenizer.json"), JsonSerializer.Serialize(new
        {
            added_tokens = QwenFimPromptBuilder.SpecialTokens.Select((token, index) => new { content = token, id = 151659 + index })
        }));
        if (metadata is not null) File.WriteAllText(Path.Combine(root, LocalModelMetadata.FileName), metadata);
        return root;
    }

    public sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "models-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
