using System.Collections.ObjectModel;
using System.Windows.Input;
using FileSort.Classification;
using FileSort.Desktop.Services;

namespace FileSort.Desktop.ViewModels;

public sealed class ColorChoice : ObservableObject
{
    private bool _isSelected;
    public ColorChoice(string hex) { Hex = hex; Brush = BrushUtil.FromHex(hex); }
    public string Hex { get; }
    public System.Windows.Media.Brush Brush { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class CategoriesViewModel : ObservableObject
{
    // Same 20-color palette idea as the reference tag dialog.
    private static readonly string[] PaletteHex =
    {
        "#F44336", "#E91E63", "#9C27B0", "#673AB7", "#3F51B5", "#2196F3", "#03A9F4", "#00BCD4", "#009688", "#4CAF50",
        "#8BC34A", "#CDDC39", "#FFEB3B", "#FFC107", "#FF9800", "#FF5722", "#795548", "#9E9E9E", "#607D8B", "#455A64"
    };

    private readonly ClassificationService _cls;
    private readonly Action _changed;
    private CategoryItemViewModel? _selected;
    private CategoryItemViewModel? _target;
    private string _editName = "";
    private string _editColor = PaletteHex[5];
    private string _message = "";
    private bool _isNew = true, _isDeleting;
    private int _usage;

    public CategoriesViewModel(ClassificationService cls, Action changed)
    {
        _cls = cls;
        _changed = changed;
        foreach (var h in PaletteHex) Palette.Add(new ColorChoice(h));
        Reload();
        if (Items.Count > 0) Select(Items[0]); else NewCategory();
    }

    public ObservableCollection<CategoryItemViewModel> Items { get; } = new();
    public ObservableCollection<ColorChoice> Palette { get; } = new();
    public ObservableCollection<CategoryItemViewModel> ReassignTargets { get; } = new();

    public ICommand SelectCommand => new RelayCommand(p => Select(p as CategoryItemViewModel));
    public ICommand NewCommand => new RelayCommand(_ => NewCategory());
    public ICommand SaveCommand => new RelayCommand(_ => Save());
    public ICommand DeleteCommand => new RelayCommand(_ => BeginDelete(), _ => !_isNew && !_isDeleting);
    public ICommand PickColorCommand => new RelayCommand(p => { if (p is ColorChoice c) EditColor = c.Hex; });
    public ICommand PickTargetCommand => new RelayCommand(p => PickTarget(p as CategoryItemViewModel));
    public ICommand ReassignCommand => new RelayCommand(_ => ConfirmReassign(), _ => _target is not null);
    public ICommand ClearAndDeleteCommand => new RelayCommand(_ => ConfirmClear());
    public ICommand CancelDeleteCommand => new RelayCommand(_ => IsDeleting = false);

    public string EditName { get => _editName; set => Set(ref _editName, value); }
    public string EditColor
    {
        get => _editColor;
        set
        {
            if (!Set(ref _editColor, value)) return;
            foreach (var c in Palette) c.IsSelected = string.Equals(c.Hex, value, StringComparison.OrdinalIgnoreCase);
            OnPropertyChanged(nameof(EditBrush));
        }
    }
    public System.Windows.Media.Brush EditBrush => BrushUtil.FromHex(_editColor);
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsNew { get => _isNew; private set => Set(ref _isNew, value); }
    public bool IsDeleting { get => _isDeleting; private set => Set(ref _isDeleting, value); }
    public string UsageText => Loc.Format("Cat_InUse", _usage);

    private void Reload(int? keepId = null)
    {
        Items.Clear();
        foreach (var c in _cls.ListCategories()) Items.Add(new CategoryItemViewModel(c));
        foreach (var i in Items) i.IsSelected = i.Id == keepId;
    }

    private void Select(CategoryItemViewModel? item)
    {
        if (item is null) return;
        IsDeleting = false;
        foreach (var i in Items) i.IsSelected = ReferenceEquals(i, item);
        _selected = item;
        IsNew = false;
        EditName = item.Name;
        EditColor = item.ColorHex;
        Message = "";
    }

    private void NewCategory()
    {
        IsDeleting = false;
        foreach (var i in Items) i.IsSelected = false;
        _selected = null;
        IsNew = true;
        EditName = "";
        EditColor = PaletteHex[5];
        Message = "";
    }

    private void Save()
    {
        try
        {
            int keep;
            if (_isNew) keep = _cls.AddCategory(EditName, EditColor).Id;
            else { keep = _selected!.Id; _cls.UpdateCategory(keep, EditName, EditColor); }
            Reload(keep);
            Select(Items.First(i => i.Id == keep));
            _changed();
            Message = Loc.Get("Cat_Saved");
        }
        catch (ClassificationException ex) { Message = Loc.Get("Cls_Err_" + ex.Code); }
        catch (Exception ex) { Log.Error("Save category failed", ex); Message = Loc.Format("Cls_Err_Unexpected", ex.Message); }
    }

    private void BeginDelete()
    {
        if (_selected is null) return;
        try
        {
            _usage = _cls.CountClassified(_selected.Id);
            if (_usage == 0)
            {
                if (!Dialogs.Confirm(Loc.Format("Cat_ConfirmDelete", _selected.Name))) return;
                FinishDelete(new CategoryDeletion(null, false));
                return;
            }
            ReassignTargets.Clear();
            foreach (var c in _cls.ListCategories().Where(x => x.Id != _selected.Id)) ReassignTargets.Add(new CategoryItemViewModel(c));
            _target = null;
            OnPropertyChanged(nameof(UsageText));
            IsDeleting = true;
        }
        catch (Exception ex) { Log.Error("Delete check failed", ex); Message = Loc.Format("Cls_Err_Unexpected", ex.Message); }
    }

    private void PickTarget(CategoryItemViewModel? item)
    {
        if (item is null) return;
        bool was = item.IsSelected;
        foreach (var t in ReassignTargets) t.IsSelected = false;
        item.IsSelected = !was;
        _target = item.IsSelected ? item : null;
        CommandManager.InvalidateRequerySuggested();
    }

    private void ConfirmReassign()
    {
        if (_target is null) return;
        FinishDelete(new CategoryDeletion(_target.Id, false));
    }

    private void ConfirmClear()
    {
        if (!Dialogs.Confirm(Loc.Format("Cat_ConfirmClear", _usage))) return;
        FinishDelete(new CategoryDeletion(null, true));
    }

    private void FinishDelete(CategoryDeletion strategy)
    {
        try
        {
            _cls.DeleteCategory(_selected!.Id, strategy);
            IsDeleting = false;
            Reload();
            if (Items.Count > 0) Select(Items[0]); else NewCategory();
            _changed();
            Message = Loc.Get("Cat_Deleted");
        }
        catch (ClassificationException ex) { Message = Loc.Get("Cls_Err_" + ex.Code); }
        catch (Exception ex) { Log.Error("Delete category failed", ex); Message = Loc.Format("Cls_Err_Unexpected", ex.Message); }
    }
}
