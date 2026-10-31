using System.IO;
using System.Text.Json;

namespace FileSort.Desktop.Services;

/// <summary>
/// Temporary UI preferences (theme, language, view mode). Stored in a small JSON file.
/// These move into the SQLite Settings table in Phase 2.
/// </summary>
public sealed class UiSettings
{
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "ar";
    public string ViewMode { get; set; } = "Details";
    public bool ShowHidden { get; set; }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileSort");
    private static string FilePath => Path.Combine(Dir, "ui-settings.json");

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath)) ?? new UiSettings();
        }
        catch (Exception ex) { Log.Warn("Could not read ui-settings.json: " + ex.Message); }
        return new UiSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) { Log.Warn("Could not save ui-settings.json: " + ex.Message); }
    }
}
