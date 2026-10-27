namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

internal static class GeneratedTokenReader
{
    // GenAI pode concluir no EOS sem anexar um token à sequência. O último id antigo não é saída nova.
    internal static bool TryRead(ReadOnlySpan<int> sequence, int previousCount, out int token)
    {
        token = default;
        if (sequence.Length == previousCount) return false;
        if (sequence.Length != previousCount + 1)
            throw new InvalidOperationException("Sequência inesperada na geração de um único token.");
        token = sequence[previousCount];
        return true;
    }
}
