using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

internal sealed class OnnxModelTokenizer(Model model) : ITokenizer, IDisposable
{
    private readonly Tokenizer _native = new(model);
    public IReadOnlyList<int> Encode(string text) { using var sequences = _native.Encode(text); return sequences[0].ToArray(); }
    public string Decode(IEnumerable<int> tokens) => _native.Decode(tokens.ToArray());
    /// <summary>O GenAI expõe <c>TokenizerStream</c>, que mantém no nativo os bytes de um caractere ainda incompleto.</summary>
    public IIncrementalDecoder CreateIncrementalDecoder() => new NativeStreamDecoder(_native.CreateStream());
    public void Dispose() => _native.Dispose();

    /// <summary>
    /// Decodificação incremental do próprio GenAI: <c>TokenizerStream.Decode(id)</c> devolve só o texto novo e devolve
    /// vazio enquanto a sequência UTF-8 não fecha, sem nunca reprocessar os tokens anteriores.
    /// </summary>
    private sealed class NativeStreamDecoder(TokenizerStream stream) : IIncrementalDecoder
    {
        public string Append(int token) => stream.Decode(token);
        /// <summary>O stream nativo não expõe descarga: bytes incompletos no fim ficam retidos, como na referência.</summary>
        public string Flush() => "";
        public void Dispose() => stream.Dispose();
    }
}
