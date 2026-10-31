using System.Globalization;
using System.Windows;

namespace FileSort.Desktop.Services;

public static class Loc
{
    public static string Get(string key) => Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, Get(key), args); }
        catch (FormatException) { return Get(key); }
    }
}
