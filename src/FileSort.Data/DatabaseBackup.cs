using Microsoft.Data.Sqlite;

namespace FileSort.Data;

public static class DatabaseBackup
{
    /// <summary>Creates a consistent copy using SQLite's online backup API and verifies it. Returns the backup path.</summary>
    public static string Create(Database db, string backupDir, string label)
    {
        Directory.CreateDirectory(backupDir);
        var dest = System.IO.Path.Combine(backupDir, $"filesort-{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.db");
        using (var src = db.Open())
        using (var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dest, Pooling = false }.ToString()))
        {
            dst.Open();
            src.BackupDatabase(dst);
        }
        if (!IntegrityOk(dest))
        {
            try { File.Delete(dest); } catch (IOException) { }
            throw new InvalidOperationException("Backup failed integrity verification.");
        }
        return dest;
    }

    /// <summary>Restores a verified backup over the live database. A safety copy of the current file is kept first.</summary>
    public static string? Restore(Database db, string backupPath, string backupDir)
    {
        if (!IntegrityOk(backupPath))
            throw new DatabaseException(DatabaseErrorKind.RestoreFailed, "The selected backup is not a valid database.", backupPath);

        string? safety = null;
        if (File.Exists(db.Path))
        {
            try { safety = Create(db, backupDir, "before-restore"); }
            catch
            {
                Directory.CreateDirectory(backupDir);
                safety = System.IO.Path.Combine(backupDir, $"filesort-before-restore-raw-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.db");
                File.Copy(db.Path, safety, overwrite: false);
            }
        }
        RestoreFile(backupPath, db.Path);
        return safety;
    }

    internal static void RestoreFile(string backupPath, string targetPath)
    {
        SqliteConnection.ClearAllPools();
        File.Copy(backupPath, targetPath, overwrite: true);
    }

    public static bool IntegrityOk(string path)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            using var c = new SqliteConnection(cs);
            c.Open();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check";
                if (cmd.ExecuteScalar() as string != "ok") return false;
            }
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_key_check";
                using var r = cmd.ExecuteReader();
                if (r.Read()) return false;
            }
            return true;
        }
        catch { return false; }
    }

    public static int GetUserVersion(string path)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var c = new SqliteConnection(cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
