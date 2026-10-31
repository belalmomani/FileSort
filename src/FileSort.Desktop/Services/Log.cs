using System.IO;

namespace FileSort.Desktop.Services;

/// <summary>Minimal file logger. Never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileSort", "logs");

    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex is null ? msg : $"{msg}: {ex}");

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "app.log"), $"{DateTime.Now:O} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
