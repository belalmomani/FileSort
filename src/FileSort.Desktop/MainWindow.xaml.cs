using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FileSort.Core.Models;
using FileSort.Desktop.Services;
using FileSort.Desktop.ViewModels;

namespace FileSort.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly GridView _gridView;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _gridView = (GridView)FileList.View;

        FileList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ViewMode)) ApplyViewMode(_vm.ViewMode);
            if (e.PropertyName is nameof(MainViewModel.ViewMode) or nameof(MainViewModel.SortField) or nameof(MainViewModel.ClassFilter)) SyncPills();
        };
        App.LanguageChanged += ApplyLanguage;
        Closed += (_, _) => App.LanguageChanged -= ApplyLanguage;

        ApplyLanguage();
        ApplyViewMode(_vm.ViewMode);
        SyncPills();
        Loaded += (_, _) => _vm.Start();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void ApplyLanguage()
    {
        FlowDirection = App.Settings.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var cols = _gridView.Columns;
        cols[0].Header = Loc.Get("Col_Name");
        cols[1].Header = Loc.Get("Col_Modified");
        cols[2].Header = Loc.Get("Col_Type");
        cols[3].Header = Loc.Get("Col_Size");
        cols[4].Header = Loc.Get("Col_Category");
        cols[5].Header = Loc.Get("Col_Priority");
    }

    private void ApplyViewMode(ViewMode mode)
    {
        switch (mode)
        {
            case ViewMode.Details:
                FileList.View = _gridView;
                FileList.ItemTemplate = null;
                FileList.ItemContainerStyle = (Style)FindResource("DetailsItemStyle");
                FileList.ItemsPanel = (ItemsPanelTemplate)FindResource("StackPanelTpl");
                ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Auto);
                break;
            case ViewMode.Icons:
            case ViewMode.Thumbnails:
                FileList.View = null;
                FileList.ItemContainerStyle = (Style)FindResource("TileItemStyle");
                FileList.ItemTemplate = (DataTemplate)FindResource(mode == ViewMode.Icons ? "IconTemplate" : "ThumbTemplate");
                FileList.ItemsPanel = (ItemsPanelTemplate)FindResource("WrapPanelTpl");
                ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Disabled);
                break;
        }
    }

    private void SyncPills()
    {
        ViewDetails.IsChecked = _vm.ViewMode == ViewMode.Details;
        ViewIcons.IsChecked = _vm.ViewMode == ViewMode.Icons;
        ViewThumbs.IsChecked = _vm.ViewMode == ViewMode.Thumbnails;
        SortName.IsChecked = _vm.SortField == SortField.Name;
        SortSize.IsChecked = _vm.SortField == SortField.Size;
        SortModified.IsChecked = _vm.SortField == SortField.Modified;
        SortCreated.IsChecked = _vm.SortField == SortField.Created;
        SortType.IsChecked = _vm.SortField == SortField.Type;
        FilterAll.IsChecked = _vm.ClassFilter == ClassFilter.All;
        FilterClassified.IsChecked = _vm.ClassFilter == ClassFilter.Classified;
        FilterUnclassified.IsChecked = _vm.ClassFilter == ClassFilter.Unclassified;
    }

    private void ViewPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t }) _vm.ViewMode = Enum.Parse<ViewMode>(t);
        SyncPills();
    }

    private void FilterPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t }) _vm.ClassFilter = Enum.Parse<ClassFilter>(t);
        SyncPills();
    }

    private LibraryWindow? _library;

    private void Categories_Click(object sender, RoutedEventArgs e)
        => new CategoriesWindow(_vm.ReloadCategories) { Owner = this }.ShowDialog();

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        if (_library is not null) { _library.Activate(); return; }
        _library = new LibraryWindow(path => _vm.NavigateCommand.Execute(path), _vm.ReloadBadges) { Owner = this };
        _library.Closed += (_, _) => _library = null;
        _library.Show();
    }

    private void SortPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t }) _vm.SortBy(Enum.Parse<SortField>(t));
        SyncPills();
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: not null } header) return;
        SortField? field = _gridView.Columns.IndexOf(header.Column) switch
        {
            0 => SortField.Name,
            1 => SortField.Modified,
            2 => SortField.Type,
            3 => SortField.Size,
            _ => null
        };
        if (field is { } f) _vm.SortBy(f);
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _vm.UpdateSelection(FileList.SelectedItems.Cast<EntryViewModel>());

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d &&
            ItemsControl.ContainerFromElement(FileList, d) is ListViewItem { DataContext: EntryViewModel entry })
        {
            _vm.OpenEntry(entry);
        }
    }

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.SelectedEntry is { } sel) { _vm.OpenEntry(sel); e.Handled = true; }
        else if (e.Key == Key.Back) { _vm.UpCommand.Execute(null); e.Handled = true; }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.L) { AddressBox.Focus(); AddressBox.SelectAll(); e.Handled = true; }
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
        => App.ApplyTheme(App.Settings.Theme == "dark" ? "light" : "dark");

    private void Lang_Click(object sender, RoutedEventArgs e)
        => App.ApplyLanguage(App.Settings.Language == "ar" ? "en" : "ar");
}
