using System.Windows.Media;

namespace FileSort.Desktop.Services;

public static class BrushUtil
{
    private static readonly Dictionary<string, Brush> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] PriorityColors = { "#8BC34A", "#CDDC39", "#FFC107", "#FF9800", "#F44336" };

    public static Brush FromHex(string hex)
    {
        if (Cache.TryGetValue(hex, out var b)) return b;
        Brush brush;
        try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { brush = new SolidColorBrush(Colors.Gray); }
        brush.Freeze();
        Cache[hex] = brush;
        return brush;
    }

    public static Brush PriorityBrush(int level) => FromHex(PriorityColors[Math.Clamp(level, 1, 5) - 1]);
}
