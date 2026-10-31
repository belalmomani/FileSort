using System.Windows;
using System.Windows.Threading;
using FileSort.Desktop.Services;

namespace FileSort.Desktop;

public partial class App : Application
{
    public static UiSettings Settings { get; private set; } = new();
    public static event Action? LanguageChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        Settings = UiSettings.Load();
        ApplyTheme(Settings.Theme, save: false);
        ApplyLanguage(Settings.Language, save: false);
        new MainWindow().Show();
    }

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
