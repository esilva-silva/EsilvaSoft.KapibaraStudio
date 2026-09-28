using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>
/// Immutable names of one scope, sorted for binary search. Prefix and camel-hump lookups are O(log n + k);
/// substring matching scans only this scope and only when the faster forms return fewer items than requested.
/// </summary>
public sealed class NameTable<T>
{
    private readonly T[] _items;
    private readonly string[] _names;
    private readonly string[] _keys;
    private readonly string[] _humps;
    private readonly int[] _humpIndexes;

    /// <param name="items">Items of the scope.</param>
    /// <param name="name">Displayed name, used for exact lookups.</param>
    /// <param name="searchKey">Optional transformation of the name used for matching, such as removing a leading $.</param>
    public NameTable(IEnumerable<T> items, Func<T, string> name, Func<string, string>? searchKey = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(name);
        var entries = items.Select(item =>
            {
                var display = name(item);
                var key = searchKey?.Invoke(display) ?? display;
                return (Item: item, Name: display, Key: key.ToUpperInvariant(), Humps: Humps(key));
            })
            .OrderBy(entry => entry.Key, StringComparer.Ordinal).ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
        _items = entries.Select(entry => entry.Item).ToArray();
        _names = entries.Select(entry => entry.Name).ToArray();
        _keys = entries.Select(entry => entry.Key).ToArray();
        var humps = entries.Select((entry, index) => (entry.Humps, Index: index))
            .OrderBy(entry => entry.Humps, StringComparer.Ordinal).ThenBy(entry => entry.Index).ToArray();
        _humps = humps.Select(entry => entry.Humps).ToArray();
        _humpIndexes = humps.Select(entry => entry.Index).ToArray();
    }

    public int Count => _items.Length;

    /// <summary>Items in search order.</summary>
    public IReadOnlyList<T> Items => _items;

    /// <summary>Case-sensitive exact name lookup.</summary>
    public bool TryGetExact(string name, [MaybeNullWhen(false)] out T item)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = name.ToUpperInvariant();
        for (var index = LowerBound(_keys, key); index < _keys.Length && string.Equals(_keys[index], key, StringComparison.Ordinal); index++)
        {
            if (!string.Equals(_names[index], name, StringComparison.Ordinal)) continue;
            item = _items[index];
            return true;
        }
        item = default;
        return false;
    }

    /// <summary>Iterations between cancellation checks in the unbounded substring scan; keeps the check cheap without letting a huge scope run unchecked.</summary>
    private const int CancellationCheckStride = 512;

    /// <summary>Appends matches in the order prefix, camel humps, substring. Each item appears at most once.</summary>
    public void Collect(string query, int maximum, Func<T, bool>? filter, Action<T, CatalogMatch> sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sink);
        if (maximum <= 0) return;
        var key = query.ToUpperInvariant();
        var count = 0;
        if (key.Length == 0)
        {
            for (var index = 0; index < _items.Length && count < maximum; index++)
            {
                if (index % CancellationCheckStride == 0) cancellationToken.ThrowIfCancellationRequested();
                if (filter?.Invoke(_items[index]) != false) { sink(_items[index], CatalogMatch.Any); count++; }
            }
            return;
        }
        // Dimensionado por `maximum` (nunca maior que a tabela): a fase de prefixo pode registrar mais índices que
        // `count` quando o filtro rejeita itens, mas nunca mais que o alcance real da tabela. Um HashSet sem capacidade
        // inicial faz vários realocamentos (~13 KB para 200 itens); pré-dimensionar evita a maior parte dessas trocas.
        var seen = new HashSet<int>(Math.Min(maximum, _keys.Length));
        for (var index = LowerBound(_keys, key); index < _keys.Length && count < maximum && _keys[index].StartsWith(key, StringComparison.Ordinal); index++)
        {
            seen.Add(index);
            if (filter?.Invoke(_items[index]) != false) { sink(_items[index], CatalogMatch.Prefix); count++; }
        }
        if (key.Length >= 2)
        {
            for (var position = LowerBound(_humps, key); position < _humps.Length && count < maximum && _humps[position].StartsWith(key, StringComparison.Ordinal); position++)
            {
                var index = _humpIndexes[position];
                if (seen.Add(index) && filter?.Invoke(_items[index]) != false) { sink(_items[index], CatalogMatch.Humps); count++; }
            }
        }
        // Only path with unbounded cost: a scan of the whole scope when the faster forms above did not fill `maximum`.
        // Checked periodically, not per item, so a huge scope never runs past cancellation but the common small-scope
        // case pays no extra overhead.
        for (var index = 0; index < _keys.Length && count < maximum; index++)
        {
            if (index % CancellationCheckStride == 0) cancellationToken.ThrowIfCancellationRequested();
            if (seen.Contains(index) || !_keys[index].Contains(key, StringComparison.Ordinal)) continue;
            seen.Add(index);
            if (filter?.Invoke(_items[index]) != false) { sink(_items[index], CatalogMatch.Substring); count++; }
        }
    }

    /// <summary>Initials of words: clienteNome → CN, Cliente.Id → CI, customer_id → CI.</summary>
    internal static string Humps(string name)
    {
        var builder = new StringBuilder();
        var boundary = true;
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (!char.IsLetterOrDigit(character)) { boundary = true; continue; }
            if (boundary || char.IsUpper(character) && !char.IsUpper(name[index - 1])) builder.Append(char.ToUpperInvariant(character));
            boundary = false;
        }
        return builder.ToString();
    }

    private static int LowerBound(string[] keys, string key)
    {
        var low = 0;
        var high = keys.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (string.CompareOrdinal(keys[middle], key) < 0) low = middle + 1; else high = middle;
        }
        return low;
    }
}
