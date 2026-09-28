using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>
/// State of the traditional completion list, separated from Avalonia controls. It preserves selection by stable
/// symbol identity whenever a provider refreshes or the local filter changes.
/// </summary>
public sealed class CompletionWindowPresenter
{
    private readonly List<CompletionItem> _all = [];
    private readonly List<CompletionItem> _visible = [];
    private int _selectedIndex = -1;

    public IReadOnlyList<CompletionItem> Items => _visible;
    public CompletionItem? Selected => _selectedIndex is >= 0 and < int.MaxValue && _selectedIndex < _visible.Count ? _visible[_selectedIndex] : null;
    public string Filter { get; private set; } = "";
    public bool IsOpen { get; private set; }

    public void Show(IEnumerable<CompletionItem> items, string filter = "")
    {
        ArgumentNullException.ThrowIfNull(items);
        var previous = Selected?.SymbolId;
        _all.Clear(); _all.AddRange(items);
        IsOpen = true;
        SetFilterCore(filter, previous);
    }

    public void SetFilter(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        SetFilterCore(filter, Selected?.SymbolId);
    }

    public CompletionItem? Move(int delta)
    {
        if (_visible.Count == 0) { _selectedIndex = -1; return null; }
        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _visible.Count - 1);
        return Selected;
    }

    public CompletionItem? Select(string symbolId)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbolId);
        var index = _visible.FindIndex(item => item.SymbolId == symbolId);
        if (index >= 0) _selectedIndex = index;
        return Selected;
    }

    public void Close()
    {
        IsOpen = false; Filter = ""; _all.Clear(); _visible.Clear(); _selectedIndex = -1;
    }

    private void SetFilterCore(string filter, string? preferredSymbolId)
    {
        Filter = filter;
        _visible.Clear();
        foreach (var item in _all)
            if (filter.Length == 0 || item.FilterText.Contains(filter, StringComparison.OrdinalIgnoreCase) || item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _visible.Add(item);
        _selectedIndex = preferredSymbolId is null ? (_visible.Count == 0 ? -1 : 0) : _visible.FindIndex(item => item.SymbolId == preferredSymbolId);
        if (_selectedIndex < 0 && _visible.Count > 0) _selectedIndex = 0;
    }
}
