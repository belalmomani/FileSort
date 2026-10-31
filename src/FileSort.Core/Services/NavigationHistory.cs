namespace FileSort.Core.Services;

public sealed class NavigationHistory
{
    private readonly List<string> _items = new();
    private int _index = -1;

    public string? Current => _index >= 0 ? _items[_index] : null;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _items.Count - 1;

    public void Navigate(string path)
    {
        if (Current is not null && string.Equals(Current, path, StringComparison.OrdinalIgnoreCase)) return;
        if (_index < _items.Count - 1) _items.RemoveRange(_index + 1, _items.Count - _index - 1);
        _items.Add(path);
        _index = _items.Count - 1;
    }

    public string? GoBack() => CanGoBack ? _items[--_index] : null;
    public string? GoForward() => CanGoForward ? _items[++_index] : null;
}
