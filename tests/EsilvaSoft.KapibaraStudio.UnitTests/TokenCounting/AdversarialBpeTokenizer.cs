using System.Text;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.TokenCounting;

/// <summary>
/// Mini-BPE real, determinístico e byte-level (UTF-8), com vocabulário sintético escrito literalmente no código.
/// </summary>
/// <remarks>
/// <para>Não é um <em>fake</em> de "1 caractere = 1 token": isso tornaria a não-composicionalidade tautológica.
/// Os identificadores 0–255 são os bytes crus; cada merge da tabela cria um identificador novo a partir de 256,
/// na ordem da tabela (a ordem é o <em>rank</em>, como em um BPE de verdade).</para>
/// <para>Os merges foram escolhidos de propósito para <strong>atravessar fronteiras típicas de bloco</strong>, cobrindo
/// ASCII, acentos pt-BR, CJK, par substituto UTF-16, marca combinante e caractere de largura zero.</para>
/// <para>Não há pré-tokenizador: nada impede um merge de cruzar espaço em branco, e a tabela inclui um merge
/// <c>" " + "t"</c> exatamente para que a porta estrutural do oráculo, sozinha, não baste.</para>
/// </remarks>
internal sealed class AdversarialBpeTokenizer : ITokenizer
{
    /// <summary>Tabela de merges em hexadecimal de bytes UTF-8; a posição na tabela é o rank.</summary>
    internal static readonly (string Left, string Right)[] MergeTable =
    [
        ("74", "74"),                 //  0: "t" + "t"  -> "tt"           (ASCII: "cat" + "tle")
        ("20", "74"),                 //  1: " " + "t"  -> " t"           (merge que cruza espaço em branco)
        ("C3", "A3"),                 //  2: -> "ã"
        ("C3", "A7"),                 //  3: -> "ç"
        ("C3A3", "6F"),               //  4: "ã" + "o"  -> "ão"           (pt-BR: "informaçã" + "o")
        ("E4", "B8"),                 //  5:
        ("E4B8", "AD"),               //  6: -> "中"
        ("E6", "96"),                 //  7:
        ("E696", "87"),               //  8: -> "文"
        ("E4B8AD", "E69687"),         //  9: "中" + "文" -> "中文"         (CJK)
        ("F0", "9F"),                 // 10:
        ("F09F", "98"),               // 11:
        ("F09F98", "80"),             // 12: -> "😀" U+1F600
        ("F09F9880", "F09F9880"),     // 13: "😀" + "😀"                  (par substituto UTF-16)
        ("CC", "81"),                 // 14: -> U+0301, acento agudo combinante
        ("65", "CC81"),               // 15: "e" + U+0301 -> "é" decomposto (marca combinante)
        ("E2", "80"),                 // 16:
        ("E280", "8B"),               // 17: -> U+200B, espaço de largura zero
        ("61", "E2808B"),             // 18: "a" + U+200B                 (largura zero)
    ];

    private readonly List<byte[]> _pieces = [];
    private readonly Dictionary<(int Left, int Right), int> _ranks = [];
    private readonly Dictionary<(int Left, int Right), int> _merged = [];

    public AdversarialBpeTokenizer()
    {
        for (var b = 0; b < 256; b++) _pieces.Add([(byte)b]);
        var byBytes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var b = 0; b < 256; b++) byBytes[Convert.ToHexString([(byte)b])] = b;
        for (var rank = 0; rank < MergeTable.Length; rank++)
        {
            var (left, right) = MergeTable[rank];
            var pair = (byBytes[left], byBytes[right]);
            var id = _pieces.Count;
            _pieces.Add([.. _pieces[pair.Item1], .. _pieces[pair.Item2]]);
            _ranks[pair] = rank;
            _merged[pair] = id;
            byBytes[left + right] = id;
        }
    }

    /// <summary>Quantidade de identificadores do vocabulário sintético.</summary>
    public int VocabularySize => _pieces.Count;

    public IReadOnlyList<int> Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ids = Encoding.UTF8.GetBytes(text).Select(b => (int)b).ToList();
        while (ids.Count > 1)
        {
            var bestRank = int.MaxValue;
            var best = -1;
            for (var i = 0; i < ids.Count - 1; i++)
                if (_ranks.TryGetValue((ids[i], ids[i + 1]), out var rank) && rank < bestRank) { bestRank = rank; best = i; }
            if (best < 0) break;
            ids[best] = _merged[(ids[best], ids[best + 1])];
            ids.RemoveAt(best + 1);
        }
        return ids;
    }

    public string Decode(IEnumerable<int> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var bytes = new List<byte>();
        foreach (var id in tokens) bytes.AddRange(_pieces[id]);
        return Encoding.UTF8.GetString([.. bytes]);
    }
}

/// <summary>Envelope que conta chamadas reais a <see cref="ITokenizer.Encode(string)"/>.</summary>
internal sealed class CountingTokenizer(ITokenizer inner) : ITokenizer
{
    public int EncodeCalls { get; private set; }
    public List<string> EncodedTexts { get; } = [];
    public IReadOnlyList<int> Encode(string text) { EncodeCalls++; EncodedTexts.Add(text); return inner.Encode(text); }
    public string Decode(IEnumerable<int> tokens) => inner.Decode(tokens);
}
