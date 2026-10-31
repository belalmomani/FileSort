using Microsoft.Data.Sqlite;

namespace FileSort.Data;

public sealed record Migration(int Version, string Name, string Sql);

public static class Migrations
{
    public static IReadOnlyList<Migration> All { get; } = new[]
    {
        new Migration(1, "InitialSchema", InitialSchema)
    };

    public static void Apply(Database db, int fromVersion, IReadOnlyList<Migration> migrations)
    {
        using var conn = db.Open();
        foreach (var m in migrations.Where(m => m.Version > fromVersion).OrderBy(m => m.Version))
        {
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = m.Sql + $"\nPRAGMA user_version = {m.Version};";
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    private const string InitialSchema = @"
CREATE TABLE Files (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Path TEXT NOT NULL COLLATE NOCASE,
    ParentPath TEXT NOT NULL COLLATE NOCASE,
    Name TEXT NOT NULL,
    IsDirectory INTEGER NOT NULL DEFAULT 0 CHECK (IsDirectory IN (0,1)),
    Size INTEGER,
    ModifiedUtc TEXT,
    IsPathValid INTEGER NOT NULL DEFAULT 1 CHECK (IsPathValid IN (0,1)),
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX UX_Files_Path ON Files(Path);
CREATE INDEX IX_Files_ParentPath ON Files(ParentPath);

CREATE TABLE Categories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL COLLATE NOCASE,
    ColorHex TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsDefault INTEGER NOT NULL DEFAULT 0,
    CreatedUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX UX_Categories_Name ON Categories(Name);

CREATE TABLE Priorities (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Level INTEGER NOT NULL UNIQUE CHECK (Level BETWEEN 1 AND 5),
    Key TEXT NOT NULL UNIQUE
);
INSERT INTO Priorities(Level, Key) VALUES (1,'VeryLow'),(2,'Low'),(3,'Medium'),(4,'High'),(5,'VeryHigh');

CREATE TABLE FileClassifications (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FileId INTEGER NOT NULL UNIQUE REFERENCES Files(Id) ON DELETE CASCADE,
    CategoryId INTEGER REFERENCES Categories(Id) ON DELETE RESTRICT,
    PriorityId INTEGER REFERENCES Priorities(Id) ON DELETE RESTRICT,
    Note TEXT,
    ClassifiedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    CHECK (CategoryId IS NOT NULL OR PriorityId IS NOT NULL OR Note IS NOT NULL)
);
CREATE INDEX IX_FileClassifications_Category ON FileClassifications(CategoryId);
CREATE INDEX IX_FileClassifications_Priority ON FileClassifications(PriorityId);

CREATE TABLE ProtectedPaths (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Path TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Kind TEXT NOT NULL CHECK (Kind IN ('Blocked','Protected')),
    Reason TEXT,
    IsBuiltIn INTEGER NOT NULL DEFAULT 0,
    CreatedUtc TEXT NOT NULL
);

CREATE TABLE MoveOperations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    StartedUtc TEXT NOT NULL,
    FinishedUtc TEXT,
    Method TEXT NOT NULL,
    ScopePath TEXT,
    Status TEXT NOT NULL,
    Summary TEXT
);

CREATE TABLE MoveOperationItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OperationId INTEGER NOT NULL REFERENCES MoveOperations(Id) ON DELETE CASCADE,
    FileId INTEGER REFERENCES Files(Id) ON DELETE SET NULL,
    FileName TEXT NOT NULL,
    OriginalPath TEXT NOT NULL,
    NewPath TEXT,
    CategoryName TEXT,
    PriorityKey TEXT,
    Status TEXT NOT NULL,
    ErrorMessage TEXT,
    Sha256 TEXT,
    ProcessedUtc TEXT
);
CREATE INDEX IX_MoveOperationItems_Operation ON MoveOperationItems(OperationId);

CREATE TABLE ConnectedDevices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DeviceId TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,
    TokenHash TEXT NOT NULL,
    PairedUtc TEXT NOT NULL,
    LastSeenUtc TEXT,
    IsRevoked INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE Settings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
";
}
