namespace FileSort.Core.Models;

public sealed record Category(int Id, string Name, string ColorHex, int SortOrder);

/// <summary>Importance level. Level 1 (VeryLow) .. 5 (VeryHigh). Key is a stable identifier used for localization.</summary>
public sealed record Priority(int Id, int Level, string Key);

/// <summary>Identity of a file/folder at the time it is classified.</summary>
public sealed record FileIdentity(
    string Path,
    string ParentPath,
    string Name,
    bool IsDirectory,
    long? Size,
    DateTime? ModifiedUtc);

public sealed record ClassificationRecord(
    long FileId,
    string Path,
    string Name,
    bool IsDirectory,
    bool IsPathValid,
    int? CategoryId,
    int? PriorityId,
    int? PriorityLevel,
    string? Note,
    DateTime UpdatedUtc);

/// <summary>Either "leave this field unchanged" (Apply=false) or "set it to Value" (Apply=true; Value may be null to clear).</summary>
public readonly record struct FieldChange<T>(bool Apply, T Value)
{
    public static FieldChange<T> Keep => default;
    public static FieldChange<T> Set(T value) => new(true, value);
}

public sealed record ClassificationChange(
    FieldChange<int?> Category,
    FieldChange<int?> Priority,
    FieldChange<string?> Note);

public sealed record ClassificationQuery(
    int? CategoryId = null,
    int? PriorityId = null,
    int? MinPriorityLevel = null,
    bool OnlyInvalid = false,
    string? Text = null,
    int Limit = 2000);

public sealed record TrackedPath(long FileId, string Path, bool IsDirectory, bool IsPathValid);
