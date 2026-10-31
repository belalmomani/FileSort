using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FileSort.Core.Models;
using FileSort.Core.Services;
using FileSort.Desktop.Services;

namespace FileSort.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxSearchResults = 5000;

    private readonly IFileSystemService _fs;
    private readonly NavigationHistory _history = new();
    private readonly List<FileSystemEntry> _raw = new();
    private IReadOnlyList<DriveEntry> _lastDrives = Array.Empty<DriveEntry>();
    private CancellationTokenSource? _searchCts;
    private int _navId;

    private string _currentPath = PathUtil.ThisPc;
    private string _addressText = "";
    private string _searchText = "";
    private string? _errorText;
    private bool _isBusy, _isSearching, _isSearchMode, _searchLimitReached;
    private bool _showHidden;
    private SortField _sortField = SortField.Name;
    private bool _sortAscending = true;
    private ViewMode _viewMode = ViewMode.Details;
    private IReadOnlyList<EntryViewModel> _selection = Array.Empty<EntryViewModel>();
    private string _statusText = "";
    private string _multiSelectionText = "";
    private string _multiSizeText = "";

    public MainViewModel(IFileSystemService? fs = null)
    {
        _fs = fs ?? new FileSystemService();
        _showHidden = App.Settings.ShowHidden;
        _viewMode = Enum.TryParse<ViewMode>(App.Settings.ViewMode, out var vm) ? vm : ViewMode.Details;

        BackCommand = new RelayCommand(_ => GoBack(), _ => _history.CanGoBack);
        ForwardCommand = new RelayCommand(_ => GoForward(), _ => _history.CanGoForward);
        UpCommand = new RelayCommand(_ => GoUp(), _ => _currentPath != PathUtil.ThisPc);
        RefreshCommand = new RelayCommand(_ => Refresh());
        NavigateCommand = new RelayCommand(p => { if (p is string s) _ = NavigateToAsync(s); });
        GoAddressCommand = new RelayCommand(_ => GoAddress());
        SearchCommand = new RelayCommand(o => _ = SearchAsync());
        ClearSearchCommand = new RelayCommand(o => _ = ClearSearchAsync());
        OpenCommand = new RelayCommand(p => OpenEntry(p as EntryViewModel));
        DismissErrorCommand = new RelayCommand(_ => ErrorText = null);

        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        App.LanguageChanged += RefreshLocalized;
    }

    // ----- collections -----
    public ObservableCollection<EntryViewModel> Entries { get; } = new();
    public ObservableCollection<Breadcrumb> Breadcrumbs { get; } = new();
    public ObservableCollection<QuickAccessItemViewModel> QuickAccess { get; } = new();
    public ObservableCollection<DriveItemViewModel> Drives { get; } = new();
    public ObservableCollection<PropertyRow> Properties { get; } = new();

    // ----- commands -----
    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand GoAddressCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand DismissErrorCommand { get; }

    // ----- bindable state -----
    public string AddressText { get => _addressText; set => Set(ref _addressText, value); }
    public string SearchText { get => _searchText; set => Set(ref _searchText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string MultiSelectionText { get => _multiSelectionText; private set => Set(ref _multiSelectionText, value); }
    public string MultiSizeText { get => _multiSizeText; private set => Set(ref _multiSizeText, value); }

    public string? ErrorText
    {
        get => _errorText;
        private set { if (Set(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_errorText);

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) { OnPropertyChanged(nameof(ShowProgress)); OnPropertyChanged(nameof(IsEmpty)); } }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set { if (Set(ref _isSearching, value)) { OnPropertyChanged(nameof(ShowProgress)); OnPropertyChanged(nameof(IsEmpty)); } }
    }

    public bool IsSearchMode
    {
        get => _isSearchMode;
        private set { if (Set(ref _isSearchMode, value)) OnPropertyChanged(nameof(EmptyText)); }
    }

    public bool ShowProgress => _isBusy || _isSearching;
    public bool IsEmpty => Entries.Count == 0 && !ShowProgress;
    public string EmptyText => Loc.Get(_isSearchMode ? "Empty_Results" : "Empty_Folder");

    public bool ShowHidden
    {
        get => _showHidden;
        set
        {
            if (!Set(ref _showHidden, value)) return;
            App.Settings.ShowHidden = value;
            App.Settings.Save();
            Refresh();
        }
    }

    public ViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (!Set(ref _viewMode, value)) return;
            App.Settings.ViewMode = value.ToString();
            App.Settings.Save();
        }
    }

    public SortField SortField
    {
        get => _sortField;
        set { if (Set(ref _sortField, value)) ApplySort(); }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set { if (Set(ref _sortAscending, value)) { OnPropertyChanged(nameof(SortDirectionGlyph)); ApplySort(); } }
    }

    public string SortDirectionGlyph => _sortAscending ? "▲" : "▼";

    public bool NoSelection => _selection.Count == 0;
    public bool HasSingleSelection => _selection.Count == 1;
    public bool HasMultiSelection => _selection.Count > 1;
    public EntryViewModel? SelectedEntry => _selection.Count == 1 ? _selection[0] : null;

    // ----- lifecycle -----
    public void Start()
    {
        LoadQuickAccess();
        LoadClassificationLists();
        _ = RefreshDrivesAsync();
        _ = NavigateToAsync(PathUtil.ThisPc);
    }

    // ----- navigation -----
    public async Task<bool> NavigateToAsync(string path, bool record = true)
    {
        CancelSearch();
        int id = ++_navId;
        bool showHidden = _showHidden;
        IsBusy = true;
        try
        {
            var (listing, map) = await Task.Run(() =>
            {
                var l = path == PathUtil.ThisPc ? _fs.ListDrives() : _fs.List(path, showHidden);
                var m = path == PathUtil.ThisPc || l.Error != ListingError.None ? EmptyMap() : LoadBadgeMap(path);
                return (l, m);
            });

            if (id != _navId) return true; // superseded by a newer navigation

            if (listing.Error != ListingError.None)
            {
                ErrorText = Describe(listing);
                return false;
            }

            ErrorText = null;
            if (record) _history.Navigate(path);
            _currentPath = path;
            _classMap = map;
            IsSearchMode = false;
            _searchLimitReached = false;
            SearchText = "";
            AddressText = path == PathUtil.ThisPc ? "" : path;
            RebuildBreadcrumbs(path);
            SetEntries(listing.Entries);
            if (path == PathUtil.ThisPc) _ = RefreshDrivesAsync();
            CommandManager.InvalidateRequerySuggested();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Navigate failed: " + path, ex);
            ErrorText = Loc.Format("Err_Io", path, ex.Message);
            return false;
        }
        finally
        {
            if (id == _navId) IsBusy = false;
        }
    }

    private async void GoBack()
    {
        var p = _history.GoBack();
        if (p is null) return;
        if (!await NavigateToAsync(p, record: false)) _history.GoForward();
    }

    private async void GoForward()
    {
        var p = _history.GoForward();
        if (p is null) return;
        if (!await NavigateToAsync(p, record: false)) _history.GoBack();
    }

    private void GoUp()
    {
        if (_currentPath == PathUtil.ThisPc) return;
        _ = NavigateToAsync(PathUtil.GetParent(_currentPath) ?? PathUtil.ThisPc);
    }

    private void Refresh()
    {
        _ = NavigateToAsync(_currentPath, record: false);
        _ = RefreshDrivesAsync();
    }

    private void GoAddress()
    {
        var text = AddressText.Trim().Trim('"');
        if (text.Length == 0) { _ = NavigateToAsync(PathUtil.ThisPc); return; }
        if (text.Length == 2 && text[1] == ':') text += "\\";

        string full;
        try { full = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(text)); }
        catch (Exception) { ErrorText = Loc.Format("Err_Invalid", text); return; }

        if (full.Length > 3) full = full.TrimEnd('\\', '/');
        if (System.IO.File.Exists(full)) { OpenFile(full); return; }
        _ = NavigateToAsync(full);
    }

    public void OpenEntry(EntryViewModel? e)
    {
        if (e is null) return;
        if (e.IsDirectory) _ = NavigateToAsync(e.FullPath);
        else OpenFile(e.FullPath);
    }

    private void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            Log.Error("Open failed: " + path, ex);
            ErrorText = Loc.Format("Err_Open", path, ex.Message);
        }
    }

    // ----- sorting -----
    public void SortBy(SortField field)
    {
        if (_sortField == field) SortAscending = !_sortAscending;
        else { _sortAscending = true; OnPropertyChanged(nameof(SortDirectionGlyph)); SortField = field; }
    }

    private void SetEntries(IEnumerable<FileSystemEntry> items)
    {
        _raw.Clear();
        _raw.AddRange(items);
        ApplySort();
    }

    private void ApplySort()
    {
        ReplaceEntries(FileSystemService.Sort(_raw, _sortField, _sortAscending));
        UpdateStatus();
    }

    private void ReplaceEntries(IEnumerable<FileSystemEntry> items)
    {
        Entries.Clear();
        foreach (var e in items)
        {
            if (!PassesFilter(e)) continue;
            var vm = new EntryViewModel(e);
            ApplyBadge(vm);
            Entries.Add(vm);
        }
    }

    // ----- search -----
    private async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (query.Length == 0) { await ClearSearchAsync(); return; }

        CancelSearch();
        var cts = _searchCts = new CancellationTokenSource();
        var token = cts.Token;
        bool showHidden = _showHidden;
        string[] roots = _currentPath == PathUtil.ThisPc
            ? _lastDrives.Where(d => d.IsReady).Select(d => d.RootPath).ToArray()
            : new[] { _currentPath };

        ErrorText = null;
        IsSearchMode = true;
        IsSearching = true;
        _searchLimitReached = false;
        _raw.Clear();
        Entries.Clear();
        UpdateStatus();

        var dispatcher = Application.Current.Dispatcher;
        try
        {
            bool limit = await Task.Run(() =>
            {
                var batch = new List<FileSystemEntry>();
                int total = 0;
                void Flush()
                {
                    if (batch.Count == 0) return;
                    var chunk = batch.ToArray();
                    batch.Clear();
                    dispatcher.BeginInvoke(new Action(() => AppendResults(chunk, token)));
                }
                foreach (var root in roots)
                {
                    foreach (var entry in _fs.Search(root, query, showHidden, token))
                    {
                        batch.Add(entry);
                        total++;
                        if (batch.Count >= 100) Flush();
                        if (total >= MaxSearchResults) { Flush(); return true; }
                    }
                }
                Flush();
                return false;
            }, token);

            if (!token.IsCancellationRequested)
            {
                _searchLimitReached = limit;
                IsSearching = false;
                await LoadBadgesForSearchAsync();
                ApplySort();
            }
        }
        catch (OperationCanceledException) { /* superseded or cancelled */ }
        catch (Exception ex)
        {
            Log.Error("Search failed", ex);
            ErrorText = Loc.Format("Err_Io", string.Join(", ", roots), ex.Message);
            IsSearching = false;
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) { _searchCts = null; IsSearching = false; }
            cts.Dispose();
        }
    }

    private void AppendResults(FileSystemEntry[] chunk, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        foreach (var e in chunk)
        {
            _raw.Add(e);
            Entries.Add(new EntryViewModel(e));
        }
        UpdateStatus();
    }

    private async Task ClearSearchAsync()
    {
        CancelSearch();
        SearchText = "";
        if (_isSearchMode) await NavigateToAsync(_currentPath, record: false);
    }

    private void CancelSearch()
    {
        var c = _searchCts;
        _searchCts = null;
        try { c?.Cancel(); } catch (ObjectDisposedException) { }
        IsSearching = false;
    }

    // ----- sidebar -----
    private void LoadQuickAccess()
    {
        QuickAccess.Clear();
        foreach (var f in _fs.GetUserFolders()) QuickAccess.Add(new QuickAccessItemViewModel(f));
    }

    private async Task RefreshDrivesAsync()
    {
        try { _lastDrives = await Task.Run(() => _fs.GetDrives()); }
        catch (Exception ex) { Log.Error("Drive refresh failed", ex); return; }
        RebuildDrives();
    }

    private void RebuildDrives()
    {
        Drives.Clear();
        foreach (var d in _lastDrives) Drives.Add(new DriveItemViewModel(d));
    }

    private void RebuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        foreach (var b in PathUtil.GetBreadcrumbs(path)) Breadcrumbs.Add(b);
    }

    // ----- selection / details -----
    public void UpdateSelection(IEnumerable<EntryViewModel> items)
    {
        _selection = items.ToList();
        OnPropertyChanged(nameof(NoSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(SelectedEntry));
        RebuildProperties();
        UpdateStatus();
        SyncClassificationPanel();
    }

    private void RebuildProperties()
    {
        Properties.Clear();
        if (SelectedEntry is { } e)
        {
            Properties.Add(new PropertyRow(Loc.Get("Prop_Path"), e.FullPath));
            Properties.Add(new PropertyRow(Loc.Get("Prop_Type"), e.TypeText));
            if (!e.IsDirectory || e.IsDrive) Properties.Add(new PropertyRow(Loc.Get("Prop_Size"), e.SizeText));
            if (e.CreatedText.Length > 0) Properties.Add(new PropertyRow(Loc.Get("Prop_Created"), e.CreatedText));
            if (e.ModifiedText.Length > 0) Properties.Add(new PropertyRow(Loc.Get("Prop_Modified"), e.ModifiedText));
            Properties.Add(new PropertyRow(Loc.Get("Prop_Attributes"), e.AttributesText));
        }
        if (_selection.Count > 1)
        {
            long total = _selection.Where(x => !x.IsDirectory).Sum(x => x.Entry.Size);
            MultiSelectionText = Loc.Format("Details_MultiCount", _selection.Count);
            MultiSizeText = Loc.Format("Details_MultiSize", SizeFormatter.Format(total));
        }
    }

    private void UpdateStatus()
    {
        string main;
        if (_isSearchMode)
        {
            main = _isSearching
                ? Loc.Format("Status_Searching", Entries.Count)
                : Loc.Format("Status_Results", Entries.Count);
            if (_searchLimitReached) main += " — " + Loc.Format("Status_ResultsLimit", MaxSearchResults);
        }
        else main = Loc.Format("Status_Items", Entries.Count);

        if (_selection.Count > 0) main += "   |   " + Loc.Format("Status_Selected", _selection.Count);
        StatusText = main;
    }

    private static string Describe(DirectoryListing l) => l.Error switch
    {
        ListingError.NotFound => Loc.Format("Err_NotFound", l.Path),
        ListingError.AccessDenied => Loc.Format("Err_Access", l.Path),
        ListingError.InvalidPath => Loc.Format("Err_Invalid", l.Path),
        _ => Loc.Format("Err_Io", l.Path, l.ErrorDetail ?? "")
    };

    private void RefreshLocalized()
    {
        LoadQuickAccess();
        ReloadClassificationLocalization();
        RebuildDrives();
        ReplaceEntries(FileSystemService.Sort(_raw, _sortField, _sortAscending));
        RebuildProperties();
        UpdateStatus();
        OnPropertyChanged(nameof(EmptyText));
    }
}
