namespace FileSort.Core.Models;

public enum DriveKind { Local, Removable, Network, Optical, Other }

public enum SortField { Name, Size, Modified, Created, Type }

public enum ListingError { None, NotFound, AccessDenied, InvalidPath, IoError }

/// <summary>A file, folder, or drive shown in the explorer. Read-only snapshot.</summary>
public sealed record FileSystemEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime Created,
    DateTime Modified,
    string Extension,
    FileAttributes Attributes)
{
    public bool IsDrive { get; init; }
    public DriveKind? DriveKind { get; init; }
    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
    public bool IsSystem => (Attributes & FileAttributes.System) != 0;
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;
    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;
}

public sealed record DriveEntry(
    string RootPath,
    string VolumeLabel,
    DriveKind Kind,
    long TotalSize,
    long FreeSpace,
    bool IsReady);

public sealed record QuickAccessFolder(string Key, string Path);

public sealed record Breadcrumb(string Name, string FullPath);

public sealed record DirectoryListing(
    string Path,
    IReadOnlyList<FileSystemEntry> Entries,
    ListingError Error,
    string? ErrorDetail)
{
    public static DirectoryListing Failure(string path, ListingError error, string? detail = null)
        => new(path, Array.Empty<FileSystemEntry>(), error, detail);
}
