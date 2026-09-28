using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>DeepSeek Coder's isolated Unicode splits followed by byte-level BPE. No native regex dependency.</summary>
public sealed class DeepSeekModelTokenizer : ITokenizer
{
    private readonly Dictionary<string, int> _vocabulary;
    private readonly Dictionary<int, string> _pieces;
    private readonly Dictionary<(string Left, string Right), int> _ranks = [];
    private readonly Dictionary<string, int> _added;
    private readonly Regex _special;
    private readonly Regex[] _splits;
    private static readonly char[] ByteCharacters = BuildByteCharacters();
    private static readonly Dictionary<char, byte> CharacterBytes = ByteCharacters.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => (byte)x.i);

    public DeepSeekModelTokenizer(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var model = root.GetProperty("model");
        var normalizer = root.GetProperty("normalizer");
        var noNormalizer = normalizer.ValueKind == JsonValueKind.Null || (normalizer.GetProperty("type").GetString() == "Sequence"
            && normalizer.GetProperty("normalizers").GetArrayLength() == 0);
        if (model.GetProperty("type").GetString() != "BPE" || !noNormalizer
            || model.GetProperty("byte_fallback").GetBoolean() || model.GetProperty("unk_token").ValueKind != JsonValueKind.Null)
            throw new NotSupportedException("Tokenizer DeepSeek incompatível.");
        _vocabulary = model.GetProperty("vocab").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal);
        _pieces = _vocabulary.ToDictionary(p => p.Value, p => p.Key);
        var rank = 0;
        foreach (var merge in model.GetProperty("merges").EnumerateArray())
        {
            var pair = merge.ValueKind == JsonValueKind.Array ? merge.EnumerateArray().Select(x => x.GetString()!).ToArray() : merge.GetString()!.Split(' ');
            if (pair.Length != 2) throw new InvalidDataException("Merge BPE inválido.");
            _ranks.Add((pair[0], pair[1]), rank++);
        }
        _added = root.GetProperty("added_tokens").EnumerateArray().ToDictionary(t => t.GetProperty("content").GetString()!, t => t.GetProperty("id").GetInt32(), StringComparer.Ordinal);
        foreach (var (text, id) in _added) _pieces[id] = text;
        _special = new Regex(string.Join("|", _added.Keys.OrderByDescending(t => t.Length).Select(Regex.Escape)), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        var splits = new List<Regex>();
        foreach (var item in root.GetProperty("pre_tokenizer").GetProperty("pretokenizers").EnumerateArray())
        {
            switch (item.GetProperty("type").GetString())
            {
                case "Split" when item.GetProperty("behavior").GetString() == "Isolated" && !item.GetProperty("invert").GetBoolean():
                    splits.Add(new Regex(item.GetProperty("pattern").GetProperty("Regex").GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
                    break;
                case "Digits" when item.GetProperty("individual_digits").GetBoolean():
                    splits.Add(new Regex(@"\p{N}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
                    break;
                case "ByteLevel" when !item.GetProperty("add_prefix_space").GetBoolean() && !item.GetProperty("use_regex").GetBoolean(): break;
                default: throw new NotSupportedException("Pré-tokenizador DeepSeek incompatível.");
            }
        }
        _splits = splits.ToArray();
    }

    public IReadOnlyList<int> Encode(string text)
    {
        var result = new List<int>();
        var position = 0;
        foreach (Match match in _special.Matches(text))
        {
            EncodeOrdinary(text[position..match.Index], result);
            result.Add(_added[match.Value]);
            position = match.Index + match.Length;
        }
        EncodeOrdinary(text[position..], result);
        return result;
    }

    private void EncodeOrdinary(string text, List<int> result)
    {
        IEnumerable<string> segments = [text];
        foreach (var regex in _splits) segments = segments.SelectMany(segment => Isolate(segment, regex)).ToArray();
        foreach (var segment in segments)
        {
            var pieces = Encoding.UTF8.GetBytes(segment).Select(b => ByteCharacters[b].ToString()).ToList();
            while (pieces.Count > 1)
            {
                var bestRank = int.MaxValue;
                var best = -1;
                for (var i = 0; i < pieces.Count - 1; i++)
                    if (_ranks.TryGetValue((pieces[i], pieces[i + 1]), out var rank) && rank < bestRank) { bestRank = rank; best = i; }
                if (best < 0) break;
                pieces[best] += pieces[best + 1];
                pieces.RemoveAt(best + 1);
            }
            result.AddRange(pieces.Select(piece => _vocabulary[piece]));
        }
    }

    private static IEnumerable<string> Isolate(string text, Regex regex)
    {
        var position = 0;
        foreach (Match match in regex.Matches(text))
        {
            if (match.Index > position) yield return text[position..match.Index];
            if (match.Length > 0) yield return match.Value;
            position = match.Index + match.Length;
        }
        if (position < text.Length) yield return text[position..];
    }

    public string Decode(IEnumerable<int> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var bytes = new List<byte>();
        foreach (var id in tokens) AppendBytes(id, bytes);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>Bytes de uma peça, na mesma regra usada pela decodificação em lote e pela incremental.</summary>
    private void AppendBytes(int id, List<byte> bytes)
    {
        var piece = _pieces[id];
        // ByteLevel also decodes added tokens. Non-byte marker strings remain literal.
        if (piece.All(CharacterBytes.ContainsKey)) bytes.AddRange(piece.Select(c => CharacterBytes[c]));
        else bytes.AddRange(Encoding.UTF8.GetBytes(piece));
    }

    /// <summary>
    /// Decodificação incremental de verdade: cada token vira bytes uma única vez e o texto sai nas fronteiras de
    /// caractere, sem redecodificar a sequência. Não há streaming nativo aqui (é BPE em .NET puro), então o estado
    /// pendente é o buffer UTF-8 compartilhado do núcleo.
    /// </summary>
    public IIncrementalDecoder CreateIncrementalDecoder() => new ByteLevelDecoder(this);

    private sealed class ByteLevelDecoder(DeepSeekModelTokenizer tokenizer) : IIncrementalDecoder
    {
        private readonly Utf8IncrementalBuffer _buffer = new();
        private readonly List<byte> _bytes = [];

        public string Append(int token)
        {
            _bytes.Clear();
            tokenizer.AppendBytes(token, _bytes);
            return _buffer.Append(CollectionsMarshal.AsSpan(_bytes));
        }

        public string Flush() => _buffer.Flush();
        public void Dispose() => _bytes.Clear();
    }

    private static char[] BuildByteCharacters()
    {
        var result = new char[256];
        var next = 256;
        for (var b = 0; b < result.Length; b++) result[b] = (char)(b is >= 33 and <= 126 or >= 161 and <= 172 or >= 174 and <= 255 ? b : next++);
        return result;
    }
}
