namespace FileSort.Data;

public sealed class SettingsStore
{
    private readonly Database _db;
    public SettingsStore(Database db) => _db = db;

    public string? Get(string key)
    {
        using var c = _db.Open();
        using var cmd = c.Cmd("SELECT Value FROM Settings WHERE Key=@k", null, ("@k", key));
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string value)
    {
        using var c = _db.Open();
        using var cmd = c.Cmd(
            "INSERT INTO Settings(Key,Value,UpdatedUtc) VALUES(@k,@v,@t) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value, UpdatedUtc=excluded.UpdatedUtc",
            null, ("@k", key), ("@v", value), ("@t", Sql.Now()));
        cmd.ExecuteNonQuery();
    }
}
