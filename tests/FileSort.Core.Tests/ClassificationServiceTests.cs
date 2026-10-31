using FileSort.Classification;
using FileSort.Core.Models;
using FileSort.Core.Services;
using FileSort.Data;

namespace FileSort.Core.Tests;

public class ClassificationServiceTests
{
    private static (ClassificationService Svc, Database Db) Make(TempDir t)
    {
        var (db, _) = TestDefaults.Create(t);
        return (new ClassificationService(db), db);
    }

    private static ClassificationChange Set(int? cat = null, int? pri = null, string? note = null, bool setCat = true, bool setPri = true, bool setNote = false)
        => new(setCat ? FieldChange<int?>.Set(cat) : FieldChange<int?>.Keep,
               setPri ? FieldChange<int?>.Set(pri) : FieldChange<int?>.Keep,
               setNote ? FieldChange<string?>.Set(note) : FieldChange<string?>.Keep);

    private static string MakeFile(TempDir t, string name, string content = "x")
    {
        var p = t.Combine(name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    // ----- categories -----
    [Fact]
    public void AddCategory_Validates_AndRejectsDuplicates_CaseInsensitive()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);

        var c = svc.AddCategory("  Work  ", "#aabbcc");
        Assert.Equal("Work", c.Name);
        Assert.Equal("#AABBCC", c.ColorHex);

        Assert.Equal(ClassificationError.NameDuplicate, Assert.Throws<ClassificationException>(() => svc.AddCategory("work", "#112233")).Code);
        Assert.Equal(ClassificationError.NameEmpty, Assert.Throws<ClassificationException>(() => svc.AddCategory("  ", "#112233")).Code);
        Assert.Equal(ClassificationError.InvalidColor, Assert.Throws<ClassificationException>(() => svc.AddCategory("X", "red")).Code);
        Assert.Equal(ClassificationError.NameTooLong, Assert.Throws<ClassificationException>(() => svc.AddCategory(new string('a', 51), "#112233")).Code);
    }

    [Fact]
    public void UpdateCategory_ChangesNameAndColor()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var c = svc.AddCategory("Old", "#111111");

        svc.UpdateCategory(c.Id, "New", "#222222");

        var updated = svc.ListCategories().Single(x => x.Id == c.Id);
        Assert.Equal("New", updated.Name);
        Assert.Equal("#222222", updated.ColorHex);
    }

    [Fact]
    public void DeleteCategory_InUse_RequiresStrategy_ThenReassigns()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var a = svc.AddCategory("A", "#111111");
        var b = svc.AddCategory("B", "#222222");
        var file = MakeFile(t, "f.txt");
        svc.Classify(new[] { file }, Set(cat: a.Id, setPri: false));

        var ex = Assert.Throws<ClassificationException>(() => svc.DeleteCategory(a.Id, new CategoryDeletion(null, false)));
        Assert.Equal(ClassificationError.StrategyRequired, ex.Code);
        Assert.Contains(svc.ListCategories(), c => c.Id == a.Id);

        var affected = svc.DeleteCategory(a.Id, new CategoryDeletion(b.Id, false));

        Assert.Equal(1, affected);
        Assert.DoesNotContain(svc.ListCategories(), c => c.Id == a.Id);
        Assert.Equal(b.Id, svc.GetByPaths(new[] { file })[file].CategoryId);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void DeleteCategory_Clear_KeepsPriority_AndDropsEmptyRecords()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var a = svc.AddCategory("A", "#111111");
        var high = svc.ListPriorities().Single(p => p.Level == 4);
        var f1 = MakeFile(t, "only-cat.txt");
        var f2 = MakeFile(t, "cat-and-pri.txt");
        svc.Classify(new[] { f1 }, Set(cat: a.Id, setPri: false));
        svc.Classify(new[] { f2 }, Set(cat: a.Id, pri: high.Id));

        svc.DeleteCategory(a.Id, new CategoryDeletion(null, true));

        var map = svc.GetByPaths(new[] { f1, f2 });
        Assert.False(map.ContainsKey(f1));
        Assert.Null(map[f2].CategoryId);
        Assert.Equal(high.Id, map[f2].PriorityId);
    }

    // ----- classify -----
    [Fact]
    public void Classify_SingleFile_AndUpsertKeepsUnchangedFields()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        var pri = svc.ListPriorities().Single(p => p.Level == 5);
        var file = MakeFile(t, "a.txt");

        svc.Classify(new[] { file }, Set(cat: cat.Id, pri: pri.Id, note: "hello", setNote: true));
        svc.Classify(new[] { file }, new ClassificationChange(FieldChange<int?>.Keep, FieldChange<int?>.Set(null), FieldChange<string?>.Keep));

        var rec = svc.GetByPaths(new[] { file })[file];
        Assert.Equal(cat.Id, rec.CategoryId);
        Assert.Null(rec.PriorityId);
        Assert.Equal("hello", rec.Note);
        Assert.Equal(1, svc.CountTracked());
    }

    [Fact]
    public void Classify_Multiple_AndFolderAsSingleItem()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        var f1 = MakeFile(t, "a.txt");
        var f2 = MakeFile(t, "b.txt");
        var folder = t.Combine("dir");
        Directory.CreateDirectory(folder);
        MakeFile(t, "dir/inner.txt");

        var result = svc.Classify(new[] { f1, f2, folder }, Set(cat: cat.Id, setPri: false));

        Assert.Equal(3, result.Applied);
        Assert.Empty(result.Failures);
        var map = svc.GetByPaths(new[] { f1, f2, folder, t.Combine("dir", "inner.txt") });
        Assert.Equal(3, map.Count);
        Assert.True(map[folder].IsDirectory);
    }

    [Fact]
    public void Classify_ClearingEverything_RemovesRecord()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        var file = MakeFile(t, "a.txt");
        svc.Classify(new[] { file }, Set(cat: cat.Id, setPri: false));

        svc.Classify(new[] { file }, Set(cat: null, setPri: false));

        Assert.Equal(0, svc.CountTracked());
    }

    [Fact]
    public void Classify_ReportsMissingPaths_AndDriveRoots()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        var missing = t.Combine("nope.txt");
        var root = Path.GetPathRoot(t.Path)!;

        var result = svc.Classify(new[] { missing, root }, Set(cat: cat.Id, setPri: false));

        Assert.Equal(0, result.Applied);
        Assert.Contains(result.Failures, f => f.Path == missing && f.Code == ClassificationError.PathNotFound);
        Assert.Contains(result.Failures, f => f.Path == root && f.Code == ClassificationError.RootNotAllowed);
    }

    [Fact]
    public void Classify_WithUnknownCategoryOrLongNote_IsRejected()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var file = MakeFile(t, "a.txt");

        Assert.Equal(ClassificationError.CategoryNotFound,
            Assert.Throws<ClassificationException>(() => svc.Classify(new[] { file }, Set(cat: 9999, setPri: false))).Code);
        Assert.Equal(ClassificationError.PriorityNotFound,
            Assert.Throws<ClassificationException>(() => svc.Classify(new[] { file }, Set(setCat: false, pri: 9999))).Code);
        Assert.Equal(ClassificationError.NoteTooLong,
            Assert.Throws<ClassificationException>(() => svc.Classify(new[] { file }, Set(setCat: false, setPri: false, note: new string('n', 1001), setNote: true))).Code);
    }

    [Fact]
    public void ClassifyFolderContents_IsRecursive_AndDoesNotFollowSymlinks()
    {
        using var t = new TempDir();
        using var outside = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        MakeFile(t, "root/a.txt");
        MakeFile(t, "root/sub/b.txt");
        MakeFile(t, "root/sub/deep/c.txt");
        File.WriteAllText(outside.Combine("secret.txt"), "x");
        try { Directory.CreateSymbolicLink(t.Combine("root", "link"), outside.Path); } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { }

        var result = svc.ClassifyFolderContents(t.Combine("root"), Set(cat: cat.Id, setPri: false), CancellationToken.None);

        Assert.Equal(3, result.Applied);
        Assert.Equal(3, svc.CountTracked());
        Assert.False(svc.GetByPaths(new[] { outside.Combine("secret.txt") }).Any());
    }

    [Fact]
    public void GetByParent_ReturnsOnlyDirectChildren_CaseInsensitive()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        var direct = MakeFile(t, "dir/a.txt");
        var nested = MakeFile(t, "dir/sub/b.txt");
        svc.Classify(new[] { direct, nested }, Set(cat: cat.Id, setPri: false));

        var map = svc.GetByParent(t.Combine("dir").ToUpperInvariant());

        Assert.Single(map);
        Assert.Equal(cat.Id, map[direct].CategoryId);
    }

    // ----- persistence across moves, verify, relink -----
    [Fact]
    public void Verify_MarksMissing_KeepsClassification_AndRestoresWhenBack()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        var file = MakeFile(t, "a.txt");
        svc.Classify(new[] { file }, Set(cat: cat.Id, setPri: false));

        var moved = file + ".moved";
        File.Move(file, moved);
        var r1 = svc.VerifyTracked(CancellationToken.None);
        var rec = svc.Query(new ClassificationQuery()).Single();

        Assert.Equal(1, r1.NowMissing);
        Assert.False(rec.IsPathValid);
        Assert.Equal(cat.Id, rec.CategoryId);
        Assert.Single(svc.Query(new ClassificationQuery(OnlyInvalid: true)));

        File.Move(moved, file);
        var r2 = svc.VerifyTracked(CancellationToken.None);
        Assert.Equal(1, r2.Restored);
        Assert.True(svc.Query(new ClassificationQuery()).Single().IsPathValid);
    }

    [Fact]
    public void Relink_MovesRecordToNewPath_KeepingClassification()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        var file = MakeFile(t, "a.txt");
        svc.Classify(new[] { file }, Set(cat: cat.Id, setPri: false));
        var newPath = t.Combine("elsewhere", "renamed.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Move(file, newPath);
        svc.VerifyTracked(CancellationToken.None);
        var id = svc.Query(new ClassificationQuery()).Single().FileId;

        svc.Relink(id, newPath);

        var rec = svc.Query(new ClassificationQuery()).Single();
        Assert.Equal(newPath, rec.Path);
        Assert.Equal("renamed.txt", rec.Name);
        Assert.True(rec.IsPathValid);
        Assert.Equal(cat.Id, rec.CategoryId);
    }

    [Fact]
    public void Relink_Rejects_AlreadyTrackedPath_WrongKind_AndMissingPath()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        var f1 = MakeFile(t, "a.txt");
        var f2 = MakeFile(t, "b.txt");
        var folder = t.Combine("dir");
        Directory.CreateDirectory(folder);
        svc.Classify(new[] { f1, f2 }, Set(cat: cat.Id, setPri: false));
        var id1 = svc.GetByPaths(new[] { f1 })[f1].FileId;

        Assert.Equal(ClassificationError.PathAlreadyTracked, Assert.Throws<ClassificationException>(() => svc.Relink(id1, f2)).Code);
        Assert.Equal(ClassificationError.KindMismatch, Assert.Throws<ClassificationException>(() => svc.Relink(id1, folder)).Code);
        Assert.Equal(ClassificationError.PathNotFound, Assert.Throws<ClassificationException>(() => svc.Relink(id1, t.Combine("ghost.txt"))).Code);
    }

    [Fact]
    public void Query_FiltersByCategoryPriorityAndText()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cats = svc.ListCategories();
        var pris = svc.ListPriorities();
        var f1 = MakeFile(t, "report_final.txt");
        var f2 = MakeFile(t, "photo.jpg");
        svc.Classify(new[] { f1 }, Set(cat: cats[0].Id, pri: pris.Single(p => p.Level == 5).Id));
        svc.Classify(new[] { f2 }, Set(cat: cats[1].Id, pri: pris.Single(p => p.Level == 2).Id));

        Assert.Single(svc.Query(new ClassificationQuery(CategoryId: cats[0].Id)));
        Assert.Single(svc.Query(new ClassificationQuery(MinPriorityLevel: 4)));
        Assert.Single(svc.Query(new ClassificationQuery(Text: "photo")));
        Assert.Single(svc.Query(new ClassificationQuery(Text: "report_fin")));   // underscore is matched literally
        Assert.Empty(svc.Query(new ClassificationQuery(Text: "rep_rt")));
        Assert.Equal(2, svc.Query(new ClassificationQuery()).Count);
    }

    [Fact]
    public void ClearClassification_RemovesRecord_ButNotTheFile()
    {
        using var t = new TempDir();
        var (svc, _) = Make(t);
        var cat = svc.ListCategories().First();
        var file = MakeFile(t, "a.txt");
        svc.Classify(new[] { file }, Set(cat: cat.Id, setPri: false));

        Assert.Equal(1, svc.ClearClassification(new[] { file }));

        Assert.Equal(0, svc.CountTracked());
        Assert.True(File.Exists(file));
    }
}

public class FolderScannerTests
{
    [Fact]
    public void Scan_CountsFilesFoldersAndSize()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.Combine("a", "b"));
        File.WriteAllText(t.Combine("a", "one.txt"), "12345");
        File.WriteAllText(t.Combine("a", "b", "two.txt"), "123");

        var r = FolderScanner.Scan(t.Path, CancellationToken.None);

        Assert.Equal(2, r.FileCount);
        Assert.Equal(2, r.FolderCount);
        Assert.Equal(8, r.TotalSize);
    }

    [Fact]
    public void Scan_Cancelled_Throws()
    {
        using var t = new TempDir();
        File.WriteAllText(t.Combine("a.txt"), "x");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => FolderScanner.Scan(t.Path, cts.Token));
    }
}
