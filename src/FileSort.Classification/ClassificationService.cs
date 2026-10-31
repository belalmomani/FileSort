using System.Text.RegularExpressions;
using FileSort.Core.Models;
using FileSort.Core.Services;
using FileSort.Data;

namespace FileSort.Classification;

public enum ClassificationError
{
    NameEmpty, NameTooLong, NameDuplicate, InvalidColor, CategoryNotFound, PriorityNotFound, NoteTooLong,
    PathNotFound, RootNotAllowed, KindMismatch, PathAlreadyTracked, StrategyRequired, ReassignInvalid
}

public sealed class ClassificationException : Exception
{
    public ClassificationError Code { get; }
    public ClassificationException(ClassificationError code) : base(code.ToString()) => Code = code;
}

public sealed record ClassifyFailure(string Path, ClassificationError Code);
public sealed record ClassifyResult(int Applied, IReadOnlyList<ClassifyFailure> Failures);
public sealed record VerifyResult(int Checked, int NowMissing, int Restored);
public sealed record CategoryDeletion(int? ReassignTo, bool ClearClassifications);

/// <summary>
/// Manual classification engine. Nothing here classifies automatically: every record is the result of an explicit
/// user action. It only reads the disk to confirm paths exist; it never moves, renames or deletes files.
/// </summary>
public sealed class ClassificationService
{
    public const int MaxNameLength = 50;
    public const int MaxNoteLength = 1000;
    private const int BatchSize = 500;
    private static readonly Regex ColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private readonly CategoryStore _cats;
    private readonly ClassificationStore _store;

    public ClassificationService(Database db)
    {
        _cats = new CategoryStore(db);
        _store = new ClassificationStore(db);
    }

    // ----- categories & priorities -----
    public IReadOnlyList<Category> ListCategories() => _cats.ListCategories();
    public IReadOnlyList<Priority> ListPriorities() => _cats.ListPriorities();
    public int CountClassified(int categoryId) => _cats.CountClassified(categoryId);

    public Category AddCategory(string name, string colorHex)
    {
        var (n, c) = ValidateCategory(name, colorHex);
        try { return _cats.AddCategory(n, c); }
        catch (DataConflictException) { throw new ClassificationException(ClassificationError.NameDuplicate); }
    }

    public void UpdateCategory(int id, string name, string colorHex)
    {
        var (n, c) = ValidateCategory(name, colorHex);
        try
        {
            if (!_cats.UpdateCategory(id, n, c)) throw new ClassificationException(ClassificationError.CategoryNotFound);
        }
        catch (DataConflictException) { throw new ClassificationException(ClassificationError.NameDuplicate); }
    }

    /// <summary>Returns how many classification records were affected. Files on disk are never touched.</summary>
    public int DeleteCategory(int id, CategoryDeletion strategy)
    {
        if (!_cats.CategoryExists(id)) throw new ClassificationException(ClassificationError.CategoryNotFound);
        try { return _cats.DeleteCategory(id, strategy.ReassignTo, strategy.ClearClassifications); }
        catch (InvalidOperationException) { throw new ClassificationException(ClassificationError.StrategyRequired); }
        catch (ArgumentException) { throw new ClassificationException(ClassificationError.ReassignInvalid); }
    }

    private static (string Name, string Color) ValidateCategory(string name, string color)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) throw new ClassificationException(ClassificationError.NameEmpty);
        if (n.Length > MaxNameLength) throw new ClassificationException(ClassificationError.NameTooLong);
        var c = (color ?? "").Trim();
        if (!ColorRegex.IsMatch(c)) throw new ClassificationException(ClassificationError.InvalidColor);
        return (n, c.ToUpperInvariant());
    }

    // ----- classifying -----
    /// <summary>Classifies the given files/folders themselves (a folder is one item). Invalid paths are reported, not skipped silently.</summary>
    public ClassifyResult Classify(IEnumerable<string> paths, ClassificationChange change)
    {
        change = ValidateChange(change);
        var ok = new List<FileIdentity>();
        var failures = new List<ClassifyFailure>();
        foreach (var p in paths)
        {
            var id = Identify(p, out var err);
            if (id is null) failures.Add(new ClassifyFailure(p, err)); else ok.Add(id);
        }
        int applied = ok.Count > 0 ? _store.ApplyChange(ok, change) : 0;
        return new ClassifyResult(applied, failures);
    }

    /// <summary>
    /// Applies the change to every FILE inside a folder (recursively, not following links).
    /// Committed in batches; if cancelled midway, completed batches remain saved.
    /// </summary>
    public ClassifyResult ClassifyFolderContents(string folder, ClassificationChange change, CancellationToken ct)
    {
        change = ValidateChange(change);
        var failures = new List<ClassifyFailure>();
        var batch = new List<FileIdentity>(BatchSize);
        int applied = 0;
        var counters = new FolderScanCounters();

        void Flush()
        {
            if (batch.Count == 0) return;
            applied += _store.ApplyChange(batch, change);
            batch.Clear();
        }

        foreach (var file in FolderScanner.EnumerateFiles(Normalize(folder), counters, ct))
        {
            var id = Identify(file, out var err);
            if (id is null) { if (failures.Count < 100) failures.Add(new ClassifyFailure(file, err)); continue; }
            batch.Add(id);
            if (batch.Count >= BatchSize) Flush();
        }
        Flush();
        return new ClassifyResult(applied, failures);
    }

    /// <summary>Removes classification records for these paths. Files on disk are untouched.</summary>
    public int ClearClassification(IEnumerable<string> paths) => _store.RemoveByPaths(paths.Select(Normalize));

    public bool RemoveRecord(long fileId) => _store.RemoveById(fileId);

    // ----- queries -----
    public Dictionary<string, ClassificationRecord> GetByParent(string parentPath) => _store.GetByParent(Normalize(parentPath));
    public Dictionary<string, ClassificationRecord> GetByPaths(IEnumerable<string> paths) => _store.GetByPaths(paths);
    public IReadOnlyList<ClassificationRecord> Query(ClassificationQuery q) => _store.Query(q);
    public int CountTracked() => _store.CountAll();

    // ----- verification and relinking -----
    public VerifyResult VerifyTracked(CancellationToken ct)
    {
        var tracked = _store.ListTracked();
        var valid = new List<long>();
        var invalid = new List<long>();
        int missing = 0, restored = 0;
        foreach (var t in tracked)
        {
            ct.ThrowIfCancellationRequested();
            bool exists = t.IsDirectory ? Directory.Exists(t.Path) : File.Exists(t.Path);
            if (exists && !t.IsPathValid) { valid.Add(t.FileId); restored++; }
            else if (!exists && t.IsPathValid) { invalid.Add(t.FileId); missing++; }
        }
        if (valid.Count + invalid.Count > 0) _store.SetValidity(valid, invalid);
        return new VerifyResult(tracked.Count, missing, restored);
    }

    public void Relink(long fileId, string newPath)
    {
        var record = _store.GetById(fileId) ?? throw new ClassificationException(ClassificationError.PathNotFound);
        var id = Identify(newPath, out var err) ?? throw new ClassificationException(err);
        if (id.IsDirectory != record.IsDirectory) throw new ClassificationException(ClassificationError.KindMismatch);
        try { _store.Relink(fileId, id); }
        catch (DataConflictException) { throw new ClassificationException(ClassificationError.PathAlreadyTracked); }
    }

    // ----- helpers -----
    private ClassificationChange ValidateChange(ClassificationChange change)
    {
        if (change.Category.Apply && change.Category.Value is int c && !_cats.ListCategories().Any(x => x.Id == c))
            throw new ClassificationException(ClassificationError.CategoryNotFound);
        if (change.Priority.Apply && change.Priority.Value is int p && !_cats.ListPriorities().Any(x => x.Id == p))
            throw new ClassificationException(ClassificationError.PriorityNotFound);
        if (change.Note.Apply)
        {
            var note = change.Note.Value?.Trim();
            if (note is { Length: > MaxNoteLength }) throw new ClassificationException(ClassificationError.NoteTooLong);
            change = change with { Note = FieldChange<string?>.Set(string.IsNullOrEmpty(note) ? null : note) };
        }
        return change;
    }

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (!string.Equals(full, root, StringComparison.Ordinal))
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    private static FileIdentity? Identify(string path, out ClassificationError error)
    {
        error = ClassificationError.PathNotFound;
        string full;
        try { full = Normalize(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }

        var parent = Path.GetDirectoryName(full);
        if (parent is null) { error = ClassificationError.RootNotAllowed; return null; }

        try
        {
            if (Directory.Exists(full))
            {
                var d = new DirectoryInfo(full);
                return new FileIdentity(full, parent, d.Name, true, null, d.LastWriteTimeUtc);
            }
            if (File.Exists(full))
            {
                var f = new FileInfo(full);
                return new FileIdentity(full, parent, f.Name, false, f.Length, f.LastWriteTimeUtc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
