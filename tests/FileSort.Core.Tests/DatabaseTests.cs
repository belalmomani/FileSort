using FileSort.Data;
using Microsoft.Data.Sqlite;

namespace FileSort.Core.Tests;

internal static class TestDefaults
{
    public static readonly IReadOnlyList<DefaultCategory> Seven = new[]
    {
        new DefaultCategory("Audio", "#9C27B0"), new DefaultCategory("Video", "#F44336"), new DefaultCategory("Images", "#4CAF50"),
        new DefaultCategory("Documents", "#2196F3"), new DefaultCategory("Programs", "#FF9800"),
        new DefaultCategory("Archives", "#795548"), new DefaultCategory("Other", "#607D8B"),
    };

    public static (Database Db, string BackupDir) Create(TempDir t)
    {
        var db = new Database(t.Combine("data", "filesort.db"));
        var backups = t.Combine("backups");
        DatabaseInitializer.Initialize(db, Seven, backups);
        return (db, backups);
    }

    public static long Scalar(Database db, string sql)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static void Exec(Database db, string sql)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

public class DatabaseTests
{
    [Fact]
    public void NewDatabase_HasSchema_Priorities_AndSeededCategories()
    {
        using var t = new TempDir();
        var (db, _) = TestDefaults.Create(t);

        Assert.Equal(7, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM Categories"));
        Assert.Equal(5, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM Priorities"));
        Assert.Equal(Migrations.All.Max(m => m.Version), DatabaseBackup.GetUserVersion(db.Path));
        foreach (var table in new[] { "Files", "Categories", "Priorities", "FileClassifications", "ProtectedPaths",
                                      "MoveOperations", "MoveOperationItems", "ConnectedDevices", "Settings" })
            Assert.Equal(1, TestDefaults.Scalar(db, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'"));
    }

    [Fact]
    public void ReInitialize_DoesNotReseed_WhenUserDeletedDefaults()
    {
        using var t = new TempDir();
        var (db, backups) = TestDefaults.Create(t);
        TestDefaults.Exec(db, "DELETE FROM Categories");

        DatabaseInitializer.Initialize(db, TestDefaults.Seven, backups);

        Assert.Equal(0, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM Categories"));
    }

    [Fact]
    public void ForeignKeys_AreEnforced()
    {
        using var t = new TempDir();
        var (db, _) = TestDefaults.Create(t);
        Assert.Throws<SqliteException>(() => TestDefaults.Exec(db,
            "INSERT INTO FileClassifications(FileId,CategoryId,ClassifiedUtc,UpdatedUtc) VALUES (999,1,'x','x')"));
    }

    [Fact]
    public void Migration_FromOlderVersion_KeepsData_AndCreatesVerifiedBackup()
    {
        using var t = new TempDir();
        var db = new Database(t.Combine("filesort.db"));
        var backups = t.Combine("backups");
        var v1 = new[] { Migrations.All[0] };
        DatabaseInitializer.Initialize(db, TestDefaults.Seven, backups, v1);
        TestDefaults.Exec(db, "INSERT INTO Settings(Key,Value,UpdatedUtc) VALUES ('keep','me','x')");

        var v2 = new[] { Migrations.All[0], new Migration(2, "AddExtra", "ALTER TABLE Categories ADD COLUMN Extra TEXT;") };
        var result = DatabaseInitializer.Initialize(db, TestDefaults.Seven, backups, v2);

        Assert.Equal(1, result.FromVersion);
        Assert.Equal(2, result.ToVersion);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.True(DatabaseBackup.IntegrityOk(result.BackupPath!));
        Assert.Equal(2, DatabaseBackup.GetUserVersion(db.Path));
        Assert.Equal(1, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM Settings WHERE Key='keep'"));
        Assert.Equal(7, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM Categories"));
    }

    [Fact]
    public void FailingMigration_RollsBack_AndKeepsOriginalData()
    {
        using var t = new TempDir();
        var db = new Database(t.Combine("filesort.db"));
        var backups = t.Combine("backups");
        var v1 = new[] { Migrations.All[0] };
        DatabaseInitializer.Initialize(db, TestDefaults.Seven, backups, v1);
        TestDefaults.Exec(db, "INSERT INTO Settings(Key,Value,UpdatedUtc) VALUES ('keep','me','x')");

        var bad = new[] { Migrations.All[0], new Migration(2, "Broken", "CREATE TABLE Good(Id INTEGER); THIS IS NOT SQL;") };
        var ex = Assert.Throws<DatabaseException>(() => DatabaseInitializer.Initialize(db, TestDefaults.Seven, backups, bad));

        Assert.Equal(DatabaseErrorKind.MigrationFailed, ex.Kind);
        Assert.Equal(1, DatabaseBackup.GetUserVersion(db.Path));
        Assert.Equal(1, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM Settings WHERE Key='keep'"));
        Assert.Equal(0, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name='Good'"));
    }

    [Fact]
    public void DatabaseNewerThanApp_IsRefused_AndNotModified()
    {
        using var t = new TempDir();
        var db = new Database(t.Combine("filesort.db"));
        var backups = t.Combine("backups");
        var v2 = new[] { Migrations.All[0], new Migration(2, "AddExtra", "ALTER TABLE Categories ADD COLUMN Extra TEXT;") };
        DatabaseInitializer.Initialize(db, TestDefaults.Seven, backups, v2);

        var ex = Assert.Throws<DatabaseException>(() => DatabaseInitializer.Initialize(db, TestDefaults.Seven, backups, new[] { Migrations.All[0] }));

        Assert.Equal(DatabaseErrorKind.TooNew, ex.Kind);
        Assert.Equal(2, DatabaseBackup.GetUserVersion(db.Path));
    }

    [Fact]
    public void CorruptFile_IsRefused_AndLeftUntouched()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.Combine("data"));
        var path = t.Combine("data", "filesort.db");
        var garbage = new byte[2048];
        new Random(1).NextBytes(garbage);
        File.WriteAllBytes(path, garbage);

        var ex = Assert.Throws<DatabaseException>(() =>
            DatabaseInitializer.Initialize(new Database(path), TestDefaults.Seven, t.Combine("backups")));

        Assert.Equal(DatabaseErrorKind.Corrupt, ex.Kind);
        Assert.Equal(garbage, File.ReadAllBytes(path));
    }

    [Fact]
    public void Backup_ThenRestore_BringsBackOldState()
    {
        using var t = new TempDir();
        var (db, backups) = TestDefaults.Create(t);
        var backup = DatabaseBackup.Create(db, backups, "manual");
        TestDefaults.Exec(db, "DELETE FROM Categories");
        Assert.Equal(0, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM Categories"));

        var safety = DatabaseBackup.Restore(db, backup, backups);

        Assert.Equal(7, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM Categories"));
        Assert.NotNull(safety);
        Assert.True(File.Exists(safety));
    }

    [Fact]
    public void Restore_OfInvalidFile_IsRejected()
    {
        using var t = new TempDir();
        var (db, backups) = TestDefaults.Create(t);
        var bogus = t.Combine("bogus.db");
        File.WriteAllText(bogus, "not a database");

        var ex = Assert.Throws<DatabaseException>(() => DatabaseBackup.Restore(db, bogus, backups));

        Assert.Equal(DatabaseErrorKind.RestoreFailed, ex.Kind);
        Assert.Equal(7, TestDefaults.Scalar(db, "SELECT COUNT(*) FROM Categories"));
    }
}
