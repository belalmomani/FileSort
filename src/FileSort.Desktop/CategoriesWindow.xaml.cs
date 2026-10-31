using System.Windows;
using FileSort.Desktop.ViewModels;

namespace FileSort.Desktop;

public partial class CategoriesWindow : Window
{
    public CategoriesWindow(Action changed)
    {
        InitializeComponent();
        FlowDirection = App.Settings.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        DataContext = new CategoriesViewModel(App.Cls, changed);
    }
}
