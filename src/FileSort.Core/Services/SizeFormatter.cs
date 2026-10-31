using System.Globalization;

namespace FileSort.Core.Services;

public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        return i == 0
            ? $"{bytes} B"
            : v.ToString("0.#", CultureInfo.InvariantCulture) + " " + Units[i];
    }
}
