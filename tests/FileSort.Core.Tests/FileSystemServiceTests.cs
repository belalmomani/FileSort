using FileSort.Core.Models;
using FileSort.Core.Services;

namespace FileSort.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filesort-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
    public void Dispose() { try { Directory.Delete(Path, true); } catch { /* best effort */ } }
}

public class FileSystemServiceTests
{
    private readonly FileSystemService _fs = new();

    [Fact]
    public void List_ReturnsFilesAndFolders_WithMetadata()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.Combine("sub"));
        File.WriteAllText(t.Combine("a.txt"), "hello");

        var r = _fs.List(t.Path, showHidden: true);

        Assert.Equal(ListingError.None, r.Error);
        Assert.Equal(2, r.Entries.Count);
        var file = Assert.Single(r.Entries, e => !e.IsDirectory);
        Assert.Equal("a.txt", file.Name);
        Assert.Equal(".txt", file.Extension);
        Assert.Equal(5, file.Size);
        Assert.Single(r.Entries, e => e.IsDirectory && e.Name == "sub");
    }

    [Fact]
    public void List_MissingPath_ReturnsNotFound()
    {
        var r = _fs.List(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filesort-nope-" + Guid.NewGuid().ToString("N")), true);
        Assert.Equal(ListingError.NotFound, r.Error);
        Assert.Empty(r.Entries);
    }

    [Fact]
    public void List_PathIsFile_ReturnsNotFound()
    {
        using var t = new TempDir();
        File.WriteAllText(t.Combine("f.txt"), "x");
        Assert.Equal(ListingError.NotFound, _fs.List(t.Combine("f.txt"), true).Error);
    }

    [Fact]
    public void Sort_PutsFoldersFirst_ThenByName()
    {
        var now = DateTime.Now;
        var items = new[]
        {
            new FileSystemEntry("b.txt", "b", false, 1, now, now, ".txt", FileAttributes.Normal),
            new FileSystemEntry("zdir", "z", true, 0, now, now, "", FileAttributes.Directory),
            new FileSystemEntry("a.txt", "a", false, 2, now, now, ".txt", FileAttributes.Normal),
        };
        var sorted = FileSystemService.Sort(items, SortField.Name, true);
        Assert.Equal(new[] { "zdir", "a.txt", "b.txt" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Sort_BySizeDescending_KeepsFoldersFirst()
    {
        var now = DateTime.Now;
        var items = new[]
        {
            new FileSystemEntry("small", "s", false, 1, now, now, "", FileAttributes.Normal),
            new FileSystemEntry("big", "b", false, 900, now, now, "", FileAttributes.Normal),
            new FileSystemEntry("dir", "d", true, 0, now, now, "", FileAttributes.Directory),
        };
        var sorted = FileSystemService.Sort(items, SortField.Size, false);
        Assert.Equal(new[] { "dir", "big", "small" }, sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Search_FindsNestedMatches_CaseInsensitive()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.Combine("x", "y"));
        File.WriteAllText(t.Combine("x", "y", "Report-2024.pdf"), "1");
        File.WriteAllText(t.Combine("other.txt"), "2");

        var hits = _fs.Search(t.Path, "report", true, CancellationToken.None).ToList();

        var hit = Assert.Single(hits);
        Assert.Equal("Report-2024.pdf", hit.Name);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsNothing()
    {
        using var t = new TempDir();
        File.WriteAllText(t.Combine("a.txt"), "1");
        Assert.Empty(_fs.Search(t.Path, "  ", true, CancellationToken.None));
    }

    [Fact]
    public void Search_Cancelled_Throws()
    {
        using var t = new TempDir();
        File.WriteAllText(t.Combine("a.txt"), "1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => _fs.Search(t.Path, "a", true, cts.Token).ToList());
    }

    [Fact]
    public void Search_DoesNotFollowDirectorySymlinks()
    {
        using var root = new TempDir();
        using var outside = new TempDir();
        File.WriteAllText(outside.Combine("secret-file.txt"), "x");

        try { Directory.CreateSymbolicLink(root.Combine("link"), outside.Path); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return; // symlink creation not permitted on this machine; cannot assert here
        }

        var hits = _fs.Search(root.Path, "secret", true, CancellationToken.None).ToList();
        Assert.Empty(hits);
    }
}

public class NavigationHistoryTests
{
    [Fact]
    public void BackAndForward_Work()
    {
        var h = new NavigationHistory();
        h.Navigate("A"); h.Navigate("B"); h.Navigate("C");
        Assert.Equal("B", h.GoBack());
        Assert.Equal("A", h.GoBack());
        Assert.Null(h.GoBack());
        Assert.Equal("B", h.GoForward());
    }

    [Fact]
    public void NavigatingAfterBack_DropsForwardHistory()
    {
        var h = new NavigationHistory();
        h.Navigate("A"); h.Navigate("B"); h.GoBack();
        h.Navigate("C");
        Assert.False(h.CanGoForward);
        Assert.Equal("A", h.GoBack());
    }

    [Fact]
    public void NavigatingToSamePath_IsIgnored()
    {
        var h = new NavigationHistory();
        h.Navigate("A"); h.Navigate("a");
        Assert.False(h.CanGoBack);
    }
}

public class PathUtilTests
{
    [Theory]
    [InlineData(@"C:\Users\x", @"C:\Users")]
    [InlineData(@"C:\Users", @"C:\")]
    [InlineData(@"C:\Users\", @"C:\")]
    [InlineData(@"C:\", null)]
    [InlineData(@"C:", null)]
    public void GetParent_Windows(string input, string? expected) => Assert.Equal(expected, PathUtil.GetParent(input));

    [Fact]
    public void Breadcrumbs_ForWindowsPath()
    {
        var crumbs = PathUtil.GetBreadcrumbs(@"C:\Users\x");
        Assert.Equal(new[] { "C:", "Users", "x" }, crumbs.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { @"C:\", @"C:\Users", @"C:\Users\x" }, crumbs.Select(c => c.FullPath).ToArray());
    }

    [Fact]
    public void Breadcrumbs_ForThisPc_AreEmpty() => Assert.Empty(PathUtil.GetBreadcrumbs(PathUtil.ThisPc));
}

public class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    public void Format_Works(long bytes, string expected) => Assert.Equal(expected, SizeFormatter.Format(bytes));
}
