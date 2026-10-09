using System.Text.Json;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>Files already read by the catalog for one candidate folder.</summary>
public sealed record ModelFolder(string Root, string ModelType, string DecoderPath, JsonElement Tokenizer,
    LocalModelMetadata? Metadata = null);
