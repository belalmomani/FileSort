using System.Windows;

namespace FileSort.Desktop.Services;

public static class Dialogs
{
    public static bool Confirm(string message)
        => MessageBox.Show(message, "FileSort", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Error(string message)
        => MessageBox.Show(message, "FileSort", MessageBoxButton.OK, MessageBoxImage.Error);
}
