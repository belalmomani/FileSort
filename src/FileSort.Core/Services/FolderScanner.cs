namespace FileSort.Core.Services;

public sealed record FolderScanResult(int FileCount, int FolderCount, int LinkCount, int InaccessibleCount, long TotalSize);

public sealed class FolderScanCounters
{
    public int Files, Folders, Links, Inaccessible;
    public long TotalSize;
    public FolderScanResult ToResult() => new(Files, Folders, Links, Inaccessible, TotalSize);
}

/// <summary>Read-only recursive folder inspection. Never follows symlinks/junctions/reparse points.</summary>
public static class FolderScanner
{
    public static FolderScanResult Scan(string root, CancellationToken ct)
    {
        var counters = new FolderScanCounters();
        foreach (var _ in EnumerateFiles(root, counters, ct)) { }
        return counters.ToResult();
    }

    public static IEnumerable<string> EnumerateFiles(string root, FolderScanCounters counters, CancellationToken ct)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = (FileAttributes)0,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false
        };
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
                counters.Inaccessible++;
                continue;
            }

            foreach (var info in children)
            {
                bool isLink = (info.Attributes & FileAttributes.ReparsePoint) != 0;
                if (info is DirectoryInfo)
                {
                    if (isLink) counters.Links++;
                    else { counters.Folders++; pending.Push(info.FullName); }
                }
                else
                {
                    if (isLink) { counters.Links++; continue; }
                    counters.Files++;
                    try { counters.TotalSize += ((FileInfo)info).Length; } catch (IOException) { }
                    yield return info.FullName;
                }
            }
        }
    }
}
