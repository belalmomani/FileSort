using System.IO;
using System.Windows;
using System.Windows.Threading;
using FileSort.Classification;
using FileSort.Data;
using FileSort.Desktop.Services;

namespace FileSort.Desktop;

public partial class App : Application
{
    public static UiSettings Settings { get; private set; } = new();
    public static Database Db { get; private set; } = null!;
    public static ClassificationService Cls { get; private set; } = null!;
    public static event Action? LanguageChanged;

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileSort");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        Settings = UiSettings.Load();
        ApplyTheme(Settings.Theme, save: false);
        ApplyLanguage(Settings.Language, save: false);

        try
        {
            Db = new Database(Path.Combine(DataDir, "filesort.db"));
            var info = DatabaseInitializer.Initialize(Db, DefaultCategories(), Path.Combine(DataDir, "backups"));
            if (info.BackupPath is not null) Log.Warn($"Database upgraded v{info.FromVersion}->v{info.ToVersion}; backup: {info.BackupPath}");
            Cls = new ClassificationService(Db);
        }
        catch (Exception ex)
        {
            Log.Error("Database initialization failed", ex);
            Dialogs.Error(Loc.Format("Err_Db", ex.Message, Path.Combine(DataDir, "filesort.db")));
            Shutdown(1);
            return;
        }

        new MainWindow().Show();
    }

    // Seed names follow the UI language at first run. After that they belong to the user and are never overwritten.
    private static IReadOnlyList<DefaultCategory> DefaultCategories() => new[]
    {
        new DefaultCategory(Loc.Get("Cat_Audio"), "#9C27B0"),
        new DefaultCategory(Loc.Get("Cat_Video"), "#F44336"),
        new DefaultCategory(Loc.Get("Cat_Images"), "#4CAF50"),
        new DefaultCategory(Loc.Get("Cat_Documents"), "#2196F3"),
        new DefaultCategory(Loc.Get("Cat_Programs"), "#FF9800"),
        new DefaultCategory(Loc.Get("Cat_Archives"), "#795548"),
        new DefaultCategory(Loc.Get("Cat_Other"), "#607D8B"),
    };

    public static void ApplyTheme(string theme, bool save = true)
    {
        var file = theme == "light" ? "Light" : "Dark";
        Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{file}.xaml", UriKind.Absolute)
        };
        Settings.Theme = file.ToLowerInvariant();
        if (save) Settings.Save();
    }

    public static void ApplyLanguage(string lang, bool save = true)
    {
        var file = lang == "en" ? "en" : "ar";
        Current.Resources.MergedDictionaries[1] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Strings/Strings.{file}.xaml", UriKind.Absolute)
        };
        Settings.Language = file;
        if (save) Settings.Save();
        LanguageChanged?.Invoke();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(Loc.Format("Err_Unexpected", e.Exception.Message), "FileSort",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
