using System.IO;
using System.Windows;
using FileSort.Desktop.Services;
using FileSort.Desktop.ViewModels;
using Microsoft.Win32;

namespace FileSort.Desktop;

public partial class LibraryWindow : Window
{
    public LibraryWindow(Action<string> navigate, Action changed)
    {
        InitializeComponent();
        FlowDirection = App.Settings.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var vm = new LibraryViewModel(App.Cls, navigate, changed) { PickNewPath = PickPath };
        DataContext = vm;
    }

    private string? PickPath(LibraryRowViewModel row)
    {
        if (row.IsDirectory)
        {
            var dlg = new OpenFolderDialog { Title = Loc.Get("Lib_PickFolder") };
            return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
        }
        var file = new OpenFileDialog
        {
            Title = Loc.Get("Lib_PickFile"),
            CheckFileExists = true,
            FileName = row.Name,
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(row.Path)) ? Path.GetDirectoryName(row.Path) : null
        };
        return file.ShowDialog(this) == true ? file.FileName : null;
    }
}
