using FileSort.Core.Models;

namespace FileSort.Core.Services;

public interface IFileSystemService
{
    IReadOnlyList<DriveEntry> GetDrives();
    IReadOnlyList<QuickAccessFolder> GetUserFolders();
    DirectoryListing ListDrives();
    DirectoryListing List(string path, bool showHidden);
    IEnumerable<FileSystemEntry> Search(string root, string query, bool showHidden, CancellationToken ct);
}

/// <summary>
/// Read-only file system access. This class never writes, moves, renames or deletes anything.
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    public IReadOnlyList<DriveEntry> GetDrives()
    {
        var list = new List<DriveEntry>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }

        foreach (var d in drives)
        {
            try
            {
                if (d.DriveType == DriveType.NoRootDirectory) continue;
                var kind = d.DriveType switch
                {
                    DriveType.Fixed => DriveKind.Local,
                    DriveType.Removable => DriveKind.Removable,
                    DriveType.Network => DriveKind.Network,
                    DriveType.CDRom => DriveKind.Optical,
                    _ => DriveKind.Other
                };
                if (d.IsReady)
                    list.Add(new DriveEntry(d.RootDirectory.FullName, d.VolumeLabel ?? "", kind, d.TotalSize, d.AvailableFreeSpace, true));
                else
                    list.Add(new DriveEntry(d.Name, "", kind, 0, 0, false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                list.Add(new DriveEntry(d.Name, "", DriveKind.Other, 0, 0, false));
            }
        }
        return list;
    }

    public IReadOnlyList<QuickAccessFolder> GetUserFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = string.IsNullOrEmpty(home) ? "" : Path.Combine(home, "Downloads");
        var candidates = new (string Key, string Path)[]
        {
            ("Home", home),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Downloads", downloads),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        };
        return candidates
            .Where(c => !string.IsNullOrEmpty(c.Path) && Directory.Exists(c.Path))
            .Select(c => new QuickAccessFolder(c.Key, c.Path))
            .ToList();
    }

    public DirectoryListing ListDrives()
    {
        var entries = GetDrives().Select(d =>
        {
            var letter = d.RootPath.TrimEnd('\\', '/');
            if (letter.Length == 0) letter = d.RootPath;
            var name = d.VolumeLabel.Length > 0 ? $"{d.VolumeLabel} ({letter})" : letter;
            return new FileSystemEntry(name, d.RootPath, true, d.TotalSize,
                DateTime.MinValue, DateTime.MinValue, "", FileAttributes.Directory)
            { IsDrive = true, DriveKind = d.Kind };
        }).ToList();
        return new DirectoryListing(PathUtil.ThisPc, entries, ListingError.None, null);
    }

    public DirectoryListing List(string path, bool showHidden)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return DirectoryListing.Failure(path, ListingError.NotFound);

            var entries = new List<FileSystemEntry>();
            foreach (var info in dir.EnumerateFileSystemInfos("*", BuildOptions(showHidden)))
            {
                var e = FromInfo(info);
                if (e is not null) entries.Add(e);
            }
            return new DirectoryListing(path, entries, ListingError.None, null);
        }
        catch (UnauthorizedAccessException ex) { return DirectoryListing.Failure(path, ListingError.AccessDenied, ex.Message); }
        catch (DirectoryNotFoundException ex) { return DirectoryListing.Failure(path, ListingError.NotFound, ex.Message); }
        catch (IOException ex) { return DirectoryListing.Failure(path, ListingError.IoError, ex.Message); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return DirectoryListing.Failure(path, ListingError.InvalidPath, ex.Message);
        }
    }

    /// <summary>
    /// Recursive name search. Does NOT follow symlinks/junctions/reparse-point directories.
    /// Inaccessible folders are skipped silently. Throws OperationCanceledException on cancel.
    /// </summary>
    public IEnumerable<FileSystemEntry> Search(string root, string query, bool showHidden, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var options = BuildOptions(showHidden);
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();

            List<FileSystemInfo> children;
            try { children = new DirectoryInfo(current).EnumerateFileSystemInfos("*", options).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                continue;
            }

            foreach (var info in children)
            {
                ct.ThrowIfCancellationRequested();
                if (info.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var e = FromInfo(info);
                    if (e is not null) yield return e;
                }
                bool isDir = info is DirectoryInfo;
                bool isLink = (info.Attributes & FileAttributes.ReparsePoint) != 0;
                if (isDir && !isLink) pending.Push(info.FullName);
            }
        }
    }

    /// <summary>Folders first, then by the chosen field. Name is the tie-breaker.</summary>
    public static IReadOnlyList<FileSystemEntry> Sort(IEnumerable<FileSystemEntry> items, SortField field, bool ascending)
    {
        var text = StringComparer.CurrentCultureIgnoreCase;
        var ordered = items.OrderByDescending(e => e.IsDirectory);
        ordered = field switch
        {
            SortField.Size => Then(ordered, e => e.Size, Comparer<long>.Default, ascending),
            SortField.Modified => Then(ordered, e => e.Modified, Comparer<DateTime>.Default, ascending),
            SortField.Created => Then(ordered, e => e.Created, Comparer<DateTime>.Default, ascending),
            SortField.Type => Then(ordered, e => e.Extension, text, ascending),
            _ => Then(ordered, e => e.Name, text, ascending),
        };
        return ordered.ThenBy(e => e.Name, text).ToList();
    }

    private static IOrderedEnumerable<FileSystemEntry> Then<TKey>(
        IOrderedEnumerable<FileSystemEntry> source, Func<FileSystemEntry, TKey> key, IComparer<TKey> comparer, bool ascending)
        => ascending ? source.ThenBy(key, comparer) : source.ThenByDescending(key, comparer);

    private static EnumerationOptions BuildOptions(bool showHidden) => new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = showHidden ? (FileAttributes)0 : FileAttributes.Hidden | FileAttributes.System
    };

    private static FileSystemEntry? FromInfo(FileSystemInfo info)
    {
        try
        {
            bool isDir = info is DirectoryInfo;
            long size = info is FileInfo fi ? fi.Length : 0;
            return new FileSystemEntry(
                info.Name, info.FullName, isDir, size,
                info.CreationTime, info.LastWriteTime,
                isDir ? "" : info.Extension.ToLowerInvariant(),
                info.Attributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
