using System.Collections.ObjectModel;
using System.Windows.Input;
using FileSort.Classification;
using FileSort.Core.Models;
using FileSort.Core.Services;
using FileSort.Desktop.Services;

namespace FileSort.Desktop.ViewModels;

public enum ClassFilter { All, Classified, Unclassified }

/// <summary>Manual classification panel, badges in the file list, and the classified/unclassified filter.</summary>
public sealed partial class MainViewModel
{
    private Dictionary<string, ClassificationRecord> _classMap = EmptyMap();
    private IReadOnlyList<Category> _categoryList = Array.Empty<Category>();
    private IReadOnlyList<Priority> _priorityList = Array.Empty<Priority>();
    private ClassFilter _classFilter = ClassFilter.All;
    private CategoryItemViewModel? _selectedCategory;
    private PriorityItemViewModel? _selectedPriority;
    private ClassificationRecord? _currentRecord;
    private string _noteText = "";
    private bool _includeContents, _isApplying;
    private string _panelMessage = "";
    private ICommand? _selectCategoryCommand, _selectPriorityCommand, _applyCommand, _clearCommand;

    private static ClassificationService Cls => App.Cls;
    private static Dictionary<string, ClassificationRecord> EmptyMap() => new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<CategoryItemViewModel> Categories { get; } = new();
    public ObservableCollection<PriorityItemViewModel> Priorities { get; } = new();

    public ICommand SelectCategoryCommand => _selectCategoryCommand ??= new RelayCommand(p => SelectCategory(p as CategoryItemViewModel));
    public ICommand SelectPriorityCommand => _selectPriorityCommand ??= new RelayCommand(p => SelectPriority(p as PriorityItemViewModel));
    public ICommand ApplyClassificationCommand => _applyCommand ??= new RelayCommand(o => _ = ApplyClassificationAsync(), o => !_isApplying);
    public ICommand ClearClassificationCommand => _clearCommand ??= new RelayCommand(o => _ = ClearClassificationAsync(), o => !_isApplying);

    public string NoteText { get => _noteText; set => Set(ref _noteText, value); }
    public bool IncludeContents { get => _includeContents; set => Set(ref _includeContents, value); }
    public string PanelMessage { get => _panelMessage; private set => Set(ref _panelMessage, value); }
    public bool IsApplying
    {
        get => _isApplying;
        private set { if (Set(ref _isApplying, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsClassifiable => _selection.Count > 0 && _selection.All(e => !e.IsDrive);
    public bool HasFolderSelected => IsClassifiable && _selection.Any(e => e.IsDirectory);
    public bool HasCategories => Categories.Count > 0;

    public ClassFilter ClassFilter
    {
        get => _classFilter;
        set { if (Set(ref _classFilter, value)) ApplySort(); }
    }

    // ----- lists -----
    private void LoadClassificationLists()
    {
        try
        {
            _categoryList = Cls.ListCategories();
            _priorityList = Cls.ListPriorities();
        }
        catch (Exception ex) { Log.Error("Loading categories failed", ex); }

        Categories.Clear();
        foreach (var c in _categoryList) Categories.Add(new CategoryItemViewModel(c));
        Priorities.Clear();
        foreach (var p in _priorityList) Priorities.Add(new PriorityItemViewModel(p));
        _selectedCategory = null;
        _selectedPriority = null;
        OnPropertyChanged(nameof(HasCategories));
    }

    private void ReloadClassificationLocalization()
    {
        LoadClassificationLists();
        foreach (var e in Entries) ApplyBadge(e);
        SyncClassificationPanel();
    }

    /// <summary>Called after the categories window changed names/colors/deleted categories.</summary>
    public void ReloadCategories()
    {
        LoadClassificationLists();
        _ = ReloadBadgesAsync();
        SyncClassificationPanel();
    }

    /// <summary>Called after the library window changed records.</summary>
    public void ReloadBadges() => _ = ReloadBadgesAsync();

    private async Task ReloadBadgesAsync()
    {
        if (_isSearchMode) await LoadBadgesForSearchAsync();
        else if (_currentPath != PathUtil.ThisPc)
        {
            var path = _currentPath;
            var map = await Task.Run(() => LoadBadgeMap(path));
            if (path != _currentPath) return;
            _classMap = map;
        }
        ApplySort();
    }

    // ----- badges -----
    private Dictionary<string, ClassificationRecord> LoadBadgeMap(string parent)
    {
        try { return Cls.GetByParent(parent); }
        catch (Exception ex) { Log.Error("Loading classifications failed", ex); return EmptyMap(); }
    }

    private async Task LoadBadgesForSearchAsync()
    {
        var paths = _raw.Select(e => e.FullPath).ToList();
        _classMap = await Task.Run(() =>
        {
            try { return Cls.GetByPaths(paths); }
            catch (Exception ex) { Log.Error("Loading classifications failed", ex); return EmptyMap(); }
        });
    }

    private void ApplyBadge(EntryViewModel vm)
    {
        if (!_classMap.TryGetValue(vm.FullPath, out var rec)) { vm.SetBadge(null, null, null, null, null); return; }
        var cat = rec.CategoryId is int cid ? _categoryList.FirstOrDefault(c => c.Id == cid) : null;
        var pri = rec.PriorityId is int pid ? _priorityList.FirstOrDefault(p => p.Id == pid) : null;
        vm.SetBadge(cat?.Name, cat?.ColorHex, pri is null ? null : Loc.Get("Pri_" + pri.Key), pri?.Level, rec.Note);
    }

    private bool PassesFilter(FileSystemEntry e)
    {
        if (e.IsDrive || _classFilter == ClassFilter.All) return true;
        bool classified = _classMap.TryGetValue(e.FullPath, out var r) && r.CategoryId is not null;
        return _classFilter == ClassFilter.Classified ? classified : !classified;
    }

    private async Task RefreshBadgesAsync(IReadOnlyCollection<string> paths)
    {
        var fresh = await Task.Run(() => Cls.GetByPaths(paths));
        foreach (var p in paths) _classMap.Remove(p);
        foreach (var kv in fresh) _classMap[kv.Key] = kv.Value;
        foreach (var e in Entries.Where(x => paths.Contains(x.FullPath, StringComparer.OrdinalIgnoreCase))) ApplyBadge(e);
        if (_classFilter != ClassFilter.All) ApplySort();
    }

    // ----- panel state -----
    private void SelectCategory(CategoryItemViewModel? item)
    {
        if (item is null) return;
        bool was = item.IsSelected;
        foreach (var c in Categories) c.IsSelected = false;
        item.IsSelected = !was;
        _selectedCategory = item.IsSelected ? item : null;
    }

    private void SelectPriority(PriorityItemViewModel? item)
    {
        if (item is null) return;
        bool was = item.IsSelected;
        foreach (var p in Priorities) p.IsSelected = false;
        item.IsSelected = !was;
        _selectedPriority = item.IsSelected ? item : null;
    }

    private void SyncClassificationPanel()
    {
        PanelMessage = "";
        IncludeContents = false;
        foreach (var c in Categories) c.IsSelected = false;
        foreach (var p in Priorities) p.IsSelected = false;
        _selectedCategory = null;
        _selectedPriority = null;
        _currentRecord = null;
        NoteText = "";

        if (_selection.Count == 1 && !_selection[0].IsDrive && _classMap.TryGetValue(_selection[0].FullPath, out var rec))
        {
            _currentRecord = rec;
            _selectedCategory = Categories.FirstOrDefault(c => c.Id == rec.CategoryId);
            if (_selectedCategory is not null) _selectedCategory.IsSelected = true;
            _selectedPriority = Priorities.FirstOrDefault(p => p.Id == rec.PriorityId);
            if (_selectedPriority is not null) _selectedPriority.IsSelected = true;
            NoteText = rec.Note ?? "";
        }
        OnPropertyChanged(nameof(IsClassifiable));
        OnPropertyChanged(nameof(HasFolderSelected));
    }

    // ----- actions -----
    private async Task ApplyClassificationAsync()
    {
        if (_isApplying || !IsClassifiable) return;
        var targets = _selection.Select(e => e.FullPath).ToList();
        bool single = targets.Count == 1;
        var note = NoteText.Trim();
        bool noteApply = single ? note != (_currentRecord?.Note ?? "") : note.Length > 0;

        var change = new ClassificationChange(
            _selectedCategory is null ? FieldChange<int?>.Keep : FieldChange<int?>.Set(_selectedCategory.Id),
            _selectedPriority is null ? FieldChange<int?>.Keep : FieldChange<int?>.Set(_selectedPriority.Id),
            noteApply ? FieldChange<string?>.Set(note.Length == 0 ? null : note) : FieldChange<string?>.Keep);

        if (!change.Category.Apply && !change.Priority.Apply && !change.Note.Apply)
        {
            PanelMessage = Loc.Get("Cls_NothingToApply");
            return;
        }

        // For folder contents the note is NOT copied to every file; only category/importance are.
        var contentChange = change with { Note = FieldChange<string?>.Keep };
        var folders = IncludeContents && (contentChange.Category.Apply || contentChange.Priority.Apply)
            ? _selection.Where(e => e.IsDirectory).Select(e => e.FullPath).ToList()
            : new List<string>();

        IsApplying = true;
        PanelMessage = Loc.Get("Cls_Working");
        try
        {
            if (folders.Count > 0)
            {
                var summary = await Task.Run(() =>
                {
                    int files = 0, subs = 0, links = 0;
                    foreach (var f in folders)
                    {
                        var r = FolderScanner.Scan(f, CancellationToken.None);
                        files += r.FileCount; subs += r.FolderCount; links += r.LinkCount;
                    }
                    return (files, subs, links);
                });
                if (!Dialogs.Confirm(Loc.Format("Cls_ConfirmContents", summary.files, summary.subs, summary.links)))
                {
                    PanelMessage = "";
                    return;
                }
            }

            var (applied, failures) = await Task.Run(() =>
            {
                var r = Cls.Classify(targets, change);
                int total = r.Applied;
                var fails = new List<ClassifyFailure>(r.Failures);
                foreach (var f in folders)
                {
                    var rr = Cls.ClassifyFolderContents(f, contentChange, CancellationToken.None);
                    total += rr.Applied;
                    fails.AddRange(rr.Failures);
                }
                return (total, fails);
            });

            await RefreshBadgesAsync(targets);
            SyncClassificationPanel();
            foreach (var f in failures.Take(20)) Log.Warn($"Classification failed for {f.Path}: {f.Code}");
            PanelMessage = failures.Count == 0
                ? Loc.Format("Cls_Applied", applied)
                : Loc.Format("Cls_AppliedWithFailures", applied, failures.Count);
        }
        catch (ClassificationException ex) { PanelMessage = Loc.Get("Cls_Err_" + ex.Code); }
        catch (Exception ex)
        {
            Log.Error("Classification failed", ex);
            PanelMessage = Loc.Format("Cls_Err_Unexpected", ex.Message);
        }
        finally { IsApplying = false; }
    }

    private async Task ClearClassificationAsync()
    {
        if (_isApplying || !IsClassifiable) return;
        var targets = _selection.Select(e => e.FullPath).ToList();
        if (targets.Count > 1 && !Dialogs.Confirm(Loc.Format("Cls_ConfirmClear", targets.Count))) return;

        IsApplying = true;
        try
        {
            int removed = await Task.Run(() => Cls.ClearClassification(targets));
            await RefreshBadgesAsync(targets);
            SyncClassificationPanel();
            PanelMessage = Loc.Format("Cls_Cleared", removed);
        }
        catch (Exception ex)
        {
            Log.Error("Clear classification failed", ex);
            PanelMessage = Loc.Format("Cls_Err_Unexpected", ex.Message);
        }
        finally { IsApplying = false; }
    }
}
