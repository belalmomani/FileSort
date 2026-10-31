using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FileSort.Data;

public enum DatabaseErrorKind { Corrupt, TooNew, MigrationFailed, RestoreFailed }

public sealed class DatabaseException : Exception
{
    public DatabaseErrorKind Kind { get; }
    public string? BackupPath { get; }
    public DatabaseException(DatabaseErrorKind kind, string message, string? backupPath = null, Exception? inner = null)
        : base(message, inner) { Kind = kind; BackupPath = backupPath; }
}

public enum DataConflictKind { DuplicateName, DuplicatePath }

public sealed class DataConflictException : Exception
{
    public DataConflictKind Kind { get; }
    public DataConflictException(DataConflictKind kind, Exception? inner = null) : base(kind.ToString(), inner) => Kind = kind;
}

/// <summary>Holds the SQLite file location and opens connections (foreign keys always ON, no pooling).</summary>
public sealed class Database
{
    public string Path { get; }
    public string ConnectionString { get; }

    public Database(string path)
    {
        Path = path;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 15
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(ConnectionString);
        c.Open();
        return c;
    }
}

internal static class Sql
{
    public static SqliteCommand Cmd(this SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] ps)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    public static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    public static DateTime ParseUtc(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static bool IsConstraint(SqliteException ex) => ex.SqliteErrorCode == 19;
}
