using System.IO;
using FileSort.Core.Models;
using FileSort.Core.Services;
using FileSort.Desktop.Services;

namespace FileSort.Desktop.ViewModels;

public enum ViewMode { Details, Icons, Thumbnails }

public sealed record PropertyRow(string Label, string Value);

public sealed class EntryViewModel
{
    private static readonly HashSet<string> ThumbExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico" };

    public EntryViewModel(FileSystemEntry entry)
    {
        Entry = entry;
        Glyph = GlyphFor(entry);
        TypeText = TypeFor(entry);
        SizeText = entry.IsDirectory && !entry.IsDrive ? "" : SizeFormatter.Format(entry.Size);
        CreatedText = FormatDate(entry.Created);
        ModifiedText = FormatDate(entry.Modified);
        AttributesText = AttributesFor(entry);
        ThumbnailPath = !entry.IsDirectory && ThumbExt.Contains(entry.Extension) ? entry.FullPath : null;
    }

    public FileSystemEntry Entry { get; }
    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsDrive => Entry.IsDrive;
    public string Glyph { get; }
    public string TypeText { get; }
    public string SizeText { get; }
    public string CreatedText { get; }
    public string ModifiedText { get; }
    public string AttributesText { get; }
    public string? ThumbnailPath { get; }
    public double Opacity => Entry.IsHidden ? 0.55 : 1.0;

    // Display icons only. This is NOT file classification (classification is always manual, Phase 2).
    private static string GlyphFor(FileSystemEntry e)
    {
        if (e.IsDrive) return "💽";
        if (e.IsDirectory) return "📁";
        return e.Extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp" or ".ico" => "🖼️",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" => "🎵",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" or ".flv" => "🎬",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "🗜️",
            ".exe" or ".msi" or ".bat" or ".cmd" => "⚙️",
            _ => "📄"
        };
    }

    private static string TypeFor(FileSystemEntry e)
    {
        if (e.IsDrive) return Loc.Get("Drive_" + (e.DriveKind ?? DriveKind.Other));
        if (e.IsDirectory) return Loc.Get("Type_Folder");
        return e.Extension.Length > 1
            ? Loc.Format("Type_File", e.Extension.TrimStart('.').ToUpperInvariant())
            : Loc.Get("Type_FileNoExt");
    }

    private static string FormatDate(DateTime d) => d.Year < 1700 ? "" : d.ToString("yyyy-MM-dd HH:mm");

    private static string AttributesFor(FileSystemEntry e)
    {
        var parts = new List<string>();
        if (e.IsReadOnly) parts.Add(Loc.Get("Attr_ReadOnly"));
        if (e.IsHidden) parts.Add(Loc.Get("Attr_Hidden"));
        if (e.IsSystem) parts.Add(Loc.Get("Attr_System"));
        if (e.IsReparsePoint) parts.Add(Loc.Get("Attr_Link"));
        return parts.Count == 0 ? Loc.Get("Attr_None") : string.Join("، ", parts);
    }
}

public sealed class DriveItemViewModel
{
    public DriveItemViewModel(DriveEntry d)
    {
        RootPath = d.RootPath;
        var letter = d.RootPath.TrimEnd('\\', '/');
        if (letter.Length == 0) letter = d.RootPath;
        var name = d.VolumeLabel.Length > 0 ? d.VolumeLabel : Loc.Get("Drive_" + d.Kind);
        Display = $"{name} ({letter})";
        FreeText = d.IsReady
            ? Loc.Format("Drive_Free", SizeFormatter.Format(d.FreeSpace), SizeFormatter.Format(d.TotalSize))
            : Loc.Get("Drive_NotReady");
        UsedPercent = d.IsReady && d.TotalSize > 0 ? 100.0 * (d.TotalSize - d.FreeSpace) / d.TotalSize : 0;
    }

    public string RootPath { get; }
    public string Display { get; }
    public string FreeText { get; }
    public double UsedPercent { get; }
    public string Glyph => "💽";
}

public sealed class QuickAccessItemViewModel
{
    public QuickAccessItemViewModel(QuickAccessFolder f)
    {
        Path = f.Path;
        Name = Loc.Get("QA_" + f.Key);
        Glyph = f.Key switch
        {
            "Home" => "🏠", "Desktop" => "🖥️", "Documents" => "📄", "Downloads" => "⬇️",
            "Pictures" => "🖼️", "Music" => "🎵", "Videos" => "🎬", _ => "📁"
        };
    }

    public string Path { get; }
    public string Name { get; }
    public string Glyph { get; }
}
