using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using FileSort.Classification;
using FileSort.Core.Models;
using FileSort.Desktop.Services;

namespace FileSort.Desktop.ViewModels;

public sealed class LibraryRowViewModel
{
    public LibraryRowViewModel(ClassificationRecord r, IReadOnlyList<Category> cats, IReadOnlyList<Priority> pris)
    {
        Record = r;
        var cat = cats.FirstOrDefault(c => c.Id == r.CategoryId);
        var pri = pris.FirstOrDefault(p => p.Id == r.PriorityId);
        CategoryName = cat?.Name;
        CategoryBrush = cat is null ? null : BrushUtil.FromHex(cat.ColorHex);
        PriorityText = pri is null ? null : Loc.Get("Pri_" + pri.Key);
        PriorityBrush = pri is null ? null : BrushUtil.PriorityBrush(pri.Level);
        StatusText = r.IsPathValid ? Loc.Get("Lib_Ok") : Loc.Get("Lib_Missing");
    }

    public ClassificationRecord Record { get; }
    public long FileId => Record.FileId;
    public string Name => Record.Name;
    public string Path => Record.Path;
    public string? Note => Record.Note;
    public bool IsDirectory => Record.IsDirectory;
    public bool IsMissing => !Record.IsPathValid;
    public string? CategoryName { get; }
    public System.Windows.Media.Brush? CategoryBrush { get; }
    public string? PriorityText { get; }
    public System.Windows.Media.Brush? PriorityBrush { get; }
    public string StatusText { get; }
    public string Glyph => Record.IsDirectory ? "📁" : "📄";
}

public sealed class LibraryViewModel : ObservableObject
{
    private const int Limit = 2000;
    private readonly ClassificationService _cls;
    private readonly Action<string> _navigate;
    private readonly Action _changed;
    private IReadOnlyList<Category> _cats = Array.Empty<Category>();
    private IReadOnlyList<Priority> _pris = Array.Empty<Priority>();
    private CategoryItemViewModel? _catFilter;
    private PriorityItemViewModel? _priFilter;
    private LibraryRowViewModel? _selected;
    private string _searchText = "";
    private bool _highOnly, _invalidOnly, _isBusy;
    private string _status = "";

    /// <summary>Set by the window: shows a file/folder picker for the given row and returns the chosen path (or null).</summary>
    public Func<LibraryRowViewModel, string?>? PickNewPath { get; set; }

    public LibraryViewModel(ClassificationService cls, Action<string> navigate, Action changed)
    {
        _cls = cls;
        _navigate = navigate;
        _changed = changed;
        _cats = cls.ListCategories();
        _pris = cls.ListPriorities();
        foreach (var c in _cats) Categories.Add(new CategoryItemViewModel(c));
        foreach (var p in _pris) Priorities.Add(new PriorityItemViewModel(p));
        _ = ReloadAsync();
    }

    public ObservableCollection<LibraryRowViewModel> Rows { get; } = new();
    public ObservableCollection<CategoryItemViewModel> Categories { get; } = new();
    public ObservableCollection<PriorityItemViewModel> Priorities { get; } = new();

    public ICommand ReloadCommand => new RelayCommand(_ => _ = ReloadAsync());
    public ICommand VerifyCommand => new RelayCommand(_ => _ = VerifyAsync(), _ => !_isBusy);
    public ICommand FilterCategoryCommand => new RelayCommand(p => FilterCategory(p as CategoryItemViewModel));
    public ICommand FilterPriorityCommand => new RelayCommand(p => FilterPriority(p as PriorityItemViewModel));
    public ICommand OpenLocationCommand => new RelayCommand(_ => OpenLocation(), _ => _selected is not null);
    public ICommand RelinkCommand => new RelayCommand(_ => Relink(), _ => _selected is not null);
    public ICommand RemoveCommand => new RelayCommand(_ => Remove(), _ => _selected is not null);

    public LibraryRowViewModel? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) _ = ReloadAsync(); }
    }
    public bool HighOnly { get => _highOnly; set { if (Set(ref _highOnly, value)) _ = ReloadAsync(); } }
    public bool InvalidOnly { get => _invalidOnly; set { if (Set(ref _invalidOnly, value)) _ = ReloadAsync(); } }
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }

    private int _reloadId;

    public async Task ReloadAsync()
    {
        int id = ++_reloadId;
        var q = new ClassificationQuery(_catFilter?.Id, _priFilter?.Id, _highOnly ? 4 : null, _invalidOnly, _searchText, Limit);
        try
        {
            var (list, total) = await Task.Run(() => (_cls.Query(q), _cls.CountTracked()));
            if (id != _reloadId) return;
            Rows.Clear();
            foreach (var r in list) Rows.Add(new LibraryRowViewModel(r, _cats, _pris));
            Status = list.Count >= Limit ? Loc.Format("Lib_CountLimited", total, Limit) : Loc.Format("Lib_Count", list.Count);
        }
        catch (Exception ex)
        {
            Log.Error("Library load failed", ex);
            Status = Loc.Format("Cls_Err_Unexpected", ex.Message);
        }
    }

    private async Task VerifyAsync()
    {
        IsBusy = true;
        Status = Loc.Get("Lib_Verifying");
        try
        {
            var r = await Task.Run(() => _cls.VerifyTracked(CancellationToken.None));
            await ReloadAsync();
            Status = Loc.Format("Lib_Verified", r.Checked, r.NowMissing, r.Restored);
        }
        catch (Exception ex)
        {
            Log.Error("Verify failed", ex);
            Status = Loc.Format("Cls_Err_Unexpected", ex.Message);
        }
        finally { IsBusy = false; }
    }

    private void FilterCategory(CategoryItemViewModel? item)
    {
        if (item is null) return;
        bool was = item.IsSelected;
        foreach (var c in Categories) c.IsSelected = false;
        item.IsSelected = !was;
        _catFilter = item.IsSelected ? item : null;
        _ = ReloadAsync();
    }

    private void FilterPriority(PriorityItemViewModel? item)
    {
        if (item is null) return;
        bool was = item.IsSelected;
        foreach (var p in Priorities) p.IsSelected = false;
        item.IsSelected = !was;
        _priFilter = item.IsSelected ? item : null;
        _ = ReloadAsync();
    }

    private void OpenLocation()
    {
        if (_selected is null) return;
        var dir = Path.GetDirectoryName(_selected.Path);
        if (dir is not null && Directory.Exists(dir)) _navigate(dir);
        else Status = Loc.Get("Lib_LocationMissing");
    }

    private void Relink()
    {
        if (_selected is null) return;
        var path = PickNewPath?.Invoke(_selected);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            _cls.Relink(_selected.FileId, path);
            _changed();
            _ = ReloadAsync();
            Status = Loc.Get("Lib_Relinked");
        }
        catch (ClassificationException ex) { Status = Loc.Get("Cls_Err_" + ex.Code); }
        catch (Exception ex) { Log.Error("Relink failed", ex); Status = Loc.Format("Cls_Err_Unexpected", ex.Message); }
    }

    private void Remove()
    {
        if (_selected is null) return;
        if (!Dialogs.Confirm(Loc.Format("Lib_ConfirmRemove", _selected.Name))) return;
        try
        {
            _cls.RemoveRecord(_selected.FileId);
            _changed();
            _ = ReloadAsync();
            Status = Loc.Get("Lib_Removed");
        }
        catch (Exception ex) { Log.Error("Remove record failed", ex); Status = Loc.Format("Cls_Err_Unexpected", ex.Message); }
    }
}
