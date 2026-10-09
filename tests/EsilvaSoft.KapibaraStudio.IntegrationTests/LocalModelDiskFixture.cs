using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Builds synthetic model packages under an exclusive integration-test directory.</summary>
internal static class LocalModelDiskFixture
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

    public static string CreateQwen3AgentModel(string directory, string folder)
    {
        var root = Path.Combine(directory, folder);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "genai_config.json"), "{\"model\":{\"type\":\"qwen3\",\"context_length\":4096,\"decoder\":{\"filename\":\"model.onnx\"}}}");
        File.WriteAllText(Path.Combine(root, "model.onnx"), "synthetic");
        File.WriteAllText(Path.Combine(root, "tokenizer_config.json"), "{}");
        File.WriteAllText(Path.Combine(root, "tokenizer.json"), JsonSerializer.Serialize(new
        {
            added_tokens = new[]
            {
                new { content = "<|im_start|>", id = 151644 },
                new { content = "<|im_end|>", id = 151645 },
            },
        }));
        File.WriteAllText(Path.Combine(root, "chat_template.jinja"), "{{ messages }}{{ tools }}");
        File.WriteAllText(Path.Combine(root, LocalModelMetadata.FileName), """
            {"name":"Synthetic Qwen3 Agent","architecture":"qwen3","capabilities":["chat","agent","tools"],
             "agent":{"promptFormat":"qwen3-chatml-tools-v1","chatTemplateFile":"chat_template.jinja","toolCallFormat":"qwen-hermes-json","toolSchemaVersion":"kapibara-tools-2026-10","maxToolCallsPerTurn":4,"maxToolResultBytes":1024},
             "generation":{"agent":{"maxAnswerTokensPerStep":64,"sampling":{"thinking":{"temperature":0.7,"topP":0.9,"topK":20},"nonThinking":{"temperature":0.2,"topP":0.8,"topK":10}}}}}
            """);
        return root;
    }
}
