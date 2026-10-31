namespace FileSort.Data;

public sealed record DefaultCategory(string Name, string ColorHex);

public sealed record InitializeResult(int FromVersion, int ToVersion, string? BackupPath, bool CreatedNew);

public static class DatabaseInitializer
{
    public const string SeededKey = "DefaultCategoriesSeeded";

    /// <summary>
    /// Creates or upgrades the database. Existing data is never deleted:
    /// backup -> verify backup -> migrate in transactions -> verify -> (restore backup on failure).
    /// </summary>
    public static InitializeResult Initialize(
        Database db,
        IReadOnlyList<DefaultCategory> defaults,
        string backupDir,
        IReadOnlyList<Migration>? migrations = null)
    {
        migrations ??= Migrations.All;
        int latest = migrations.Max(m => m.Version);

        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(db.Path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        bool existed = File.Exists(db.Path) && new FileInfo(db.Path).Length > 0;
        int from = 0;
        string? backup = null;

        if (existed)
        {
            if (!DatabaseBackup.IntegrityOk(db.Path))
                throw new DatabaseException(DatabaseErrorKind.Corrupt,
                    "The database file failed its integrity check. It was not modified.");

            from = DatabaseBackup.GetUserVersion(db.Path);
            if (from > latest)
                throw new DatabaseException(DatabaseErrorKind.TooNew,
                    $"The database is version {from} but this app supports up to {latest}. Update the app. The file was not modified.");

            if (from > 0 && from < latest)
                backup = DatabaseBackup.Create(db, backupDir, $"pre-migration-v{from}");
        }

        try
        {
            Migrations.Apply(db, from, migrations);
            if (!DatabaseBackup.IntegrityOk(db.Path))
                throw new InvalidOperationException("Integrity check failed after migration.");
        }
        catch (Exception ex)
        {
            if (backup is not null)
            {
                try { DatabaseBackup.RestoreFile(backup, db.Path); }
                catch (Exception rex)
                {
                    throw new DatabaseException(DatabaseErrorKind.RestoreFailed,
                        $"Migration failed and automatic restore failed. Your backup is at: {backup}", backup, rex);
                }
            }
            else if (!existed)
            {
                try { File.Delete(db.Path); } catch (IOException) { }
            }
            throw new DatabaseException(DatabaseErrorKind.MigrationFailed,
                "Database migration failed" + (backup is null ? "." : $"; the previous data was restored from {backup}."),
                backup, ex);
        }

        SeedDefaultsOnce(db, defaults);
        return new InitializeResult(from, latest, backup, !existed);
    }

    private static void SeedDefaultsOnce(Database db, IReadOnlyList<DefaultCategory> defaults)
    {
        var settings = new SettingsStore(db);
        if (settings.Get(SeededKey) is not null) return;
        new CategoryStore(db).SeedDefaults(defaults);
        settings.Set(SeededKey, "1");
    }
}
