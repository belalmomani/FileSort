using FileSort.Core.Models;
using Microsoft.Data.Sqlite;

namespace FileSort.Data;

public sealed class ClassificationStore
{
    private const string SelectRecords = @"
SELECT f.Id, f.Path, f.Name, f.IsDirectory, f.IsPathValid, fc.CategoryId, fc.PriorityId, p.Level, fc.Note, fc.UpdatedUtc
FROM Files f
JOIN FileClassifications fc ON fc.FileId = f.Id
LEFT JOIN Priorities p ON p.Id = fc.PriorityId";

    private readonly Database _db;
    public ClassificationStore(Database db) => _db = db;

    /// <summary>Applies a change to many items in ONE transaction. Fields marked Keep are left as they were.</summary>
    public int ApplyChange(IReadOnlyList<FileIdentity> items, ClassificationChange change)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        var now = Sql.Now();
        int n = 0;
        foreach (var it in items)
        {
            long fileId = UpsertFile(c, tx, it, now);
            int? cat = null, pri = null;
            string? note = null;
            bool exists = false;
            using (var q = c.Cmd("SELECT CategoryId, PriorityId, Note FROM FileClassifications WHERE FileId=@f", tx, ("@f", fileId)))
            using (var r = q.ExecuteReader())
            {
                if (r.Read())
                {
                    exists = true;
                    cat = r.IsDBNull(0) ? null : r.GetInt32(0);
                    pri = r.IsDBNull(1) ? null : r.GetInt32(1);
                    note = r.IsDBNull(2) ? null : r.GetString(2);
                }
            }

            if (change.Category.Apply) cat = change.Category.Value;
            if (change.Priority.Apply) pri = change.Priority.Value;
            if (change.Note.Apply) note = change.Note.Value;

            if (cat is null && pri is null && note is null)
            {
                // Nothing left to remember: drop the record (and the identity row; the file itself is untouched).
                using var del = c.Cmd("DELETE FROM Files WHERE Id=@f", tx, ("@f", fileId));
                del.ExecuteNonQuery();
            }
            else if (exists)
            {
                using var up = c.Cmd("UPDATE FileClassifications SET CategoryId=@c, PriorityId=@p, Note=@n, UpdatedUtc=@t WHERE FileId=@f", tx,
                    ("@c", cat), ("@p", pri), ("@n", note), ("@t", now), ("@f", fileId));
                up.ExecuteNonQuery();
            }
            else
            {
                using var ins = c.Cmd("INSERT INTO FileClassifications(FileId,CategoryId,PriorityId,Note,ClassifiedUtc,UpdatedUtc) VALUES(@f,@c,@p,@n,@t,@t)", tx,
                    ("@f", fileId), ("@c", cat), ("@p", pri), ("@n", note), ("@t", now));
                ins.ExecuteNonQuery();
            }
            n++;
        }
        tx.Commit();
        return n;
    }

    private static long UpsertFile(SqliteConnection c, SqliteTransaction tx, FileIdentity it, string now)
    {
        using (var up = c.Cmd(@"
INSERT INTO Files(Path,ParentPath,Name,IsDirectory,Size,ModifiedUtc,IsPathValid,CreatedUtc,UpdatedUtc)
VALUES(@p,@pp,@n,@d,@s,@m,1,@t,@t)
ON CONFLICT(Path) DO UPDATE SET ParentPath=excluded.ParentPath, Name=excluded.Name, IsDirectory=excluded.IsDirectory,
    Size=excluded.Size, ModifiedUtc=excluded.ModifiedUtc, IsPathValid=1, UpdatedUtc=excluded.UpdatedUtc", tx,
            ("@p", it.Path), ("@pp", it.ParentPath), ("@n", it.Name), ("@d", it.IsDirectory ? 1 : 0),
            ("@s", it.Size), ("@m", it.ModifiedUtc?.ToString("o")), ("@t", now)))
            up.ExecuteNonQuery();
        using var sel = c.Cmd("SELECT Id FROM Files WHERE Path=@p", tx, ("@p", it.Path));
        return Convert.ToInt64(sel.ExecuteScalar());
    }

    public int RemoveByPaths(IEnumerable<string> paths)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        int n = 0;
        foreach (var p in paths)
        {
            using var cmd = c.Cmd("DELETE FROM Files WHERE Path=@p", tx, ("@p", p));
            n += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return n;
    }

    public bool RemoveById(long fileId)
    {
        using var c = _db.Open();
        using var cmd = c.Cmd("DELETE FROM Files WHERE Id=@i", null, ("@i", fileId));
        return cmd.ExecuteNonQuery() > 0;
    }

    public ClassificationRecord? GetById(long fileId)
    {
        using var c = _db.Open();
        using var cmd = c.Cmd(SelectRecords + " WHERE f.Id=@i", null, ("@i", fileId));
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public Dictionary<string, ClassificationRecord> GetByParent(string parentPath)
    {
        using var c = _db.Open();
        using var cmd = c.Cmd(SelectRecords + " WHERE f.ParentPath=@pp", null, ("@pp", parentPath));
        return ReadAll(cmd);
    }

    public Dictionary<string, ClassificationRecord> GetByPaths(IEnumerable<string> paths)
    {
        var result = new Dictionary<string, ClassificationRecord>(StringComparer.OrdinalIgnoreCase);
        using var c = _db.Open();
        foreach (var chunk in paths.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(400))
        {
            var names = chunk.Select((_, i) => "@p" + i).ToArray();
            using var cmd = c.Cmd(SelectRecords + $" WHERE f.Path IN ({string.Join(",", names)})", null,
                chunk.Select((p, i) => (names[i], (object?)p)).ToArray());
            foreach (var kv in ReadAll(cmd)) result[kv.Key] = kv.Value;
        }
        return result;
    }

    public IReadOnlyList<ClassificationRecord> Query(ClassificationQuery q)
    {
        var where = new List<string>();
        var ps = new List<(string, object?)>();
        if (q.CategoryId is int cat) { where.Add("fc.CategoryId=@cat"); ps.Add(("@cat", cat)); }
        if (q.PriorityId is int pri) { where.Add("fc.PriorityId=@pri"); ps.Add(("@pri", pri)); }
        if (q.MinPriorityLevel is int min) { where.Add("p.Level>=@min"); ps.Add(("@min", min)); }
        if (q.OnlyInvalid) where.Add("f.IsPathValid=0");
        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            var esc = q.Text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            where.Add("(f.Name LIKE @txt ESCAPE '\\' OR f.Path LIKE @txt ESCAPE '\\')");
            ps.Add(("@txt", "%" + esc + "%"));
        }
        var sql = SelectRecords + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") +
                  " ORDER BY f.Name COLLATE NOCASE LIMIT @lim";
        ps.Add(("@lim", q.Limit));

        using var c = _db.Open();
        using var cmd = c.Cmd(sql, null, ps.ToArray());
        using var r = cmd.ExecuteReader();
        var list = new List<ClassificationRecord>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public int CountAll()
    {
        using var c = _db.Open();
        using var cmd = c.Cmd("SELECT COUNT(*) FROM FileClassifications");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public IReadOnlyList<TrackedPath> ListTracked()
    {
        using var c = _db.Open();
        using var cmd = c.Cmd("SELECT f.Id, f.Path, f.IsDirectory, f.IsPathValid FROM Files f JOIN FileClassifications fc ON fc.FileId=f.Id");
        using var r = cmd.ExecuteReader();
        var list = new List<TrackedPath>();
        while (r.Read()) list.Add(new TrackedPath(r.GetInt64(0), r.GetString(1), r.GetInt32(2) == 1, r.GetInt32(3) == 1));
        return list;
    }

    public void SetValidity(IEnumerable<long> validIds, IEnumerable<long> invalidIds)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        var now = Sql.Now();
        foreach (var (ids, flag) in new[] { (validIds, 1), (invalidIds, 0) })
            foreach (var id in ids)
            {
                using var cmd = c.Cmd("UPDATE Files SET IsPathValid=@v, UpdatedUtc=@t WHERE Id=@i", tx, ("@v", flag), ("@t", now), ("@i", id));
                cmd.ExecuteNonQuery();
            }
        tx.Commit();
    }

    /// <summary>Points an existing record at a new path (after the user moved the file outside the app).</summary>
    public bool Relink(long fileId, FileIdentity newIdentity)
    {
        try
        {
            using var c = _db.Open();
            using var cmd = c.Cmd(@"UPDATE Files SET Path=@p, ParentPath=@pp, Name=@n, IsDirectory=@d, Size=@s, ModifiedUtc=@m, IsPathValid=1, UpdatedUtc=@t WHERE Id=@i", null,
                ("@p", newIdentity.Path), ("@pp", newIdentity.ParentPath), ("@n", newIdentity.Name), ("@d", newIdentity.IsDirectory ? 1 : 0),
                ("@s", newIdentity.Size), ("@m", newIdentity.ModifiedUtc?.ToString("o")), ("@t", Sql.Now()), ("@i", fileId));
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (SqliteException ex) when (Sql.IsConstraint(ex))
        {
            throw new DataConflictException(DataConflictKind.DuplicatePath, ex);
        }
    }

    private static Dictionary<string, ClassificationRecord> ReadAll(SqliteCommand cmd)
    {
        var map = new Dictionary<string, ClassificationRecord>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var rec = Read(r);
            map[rec.Path] = rec;
        }
        return map;
    }

    private static ClassificationRecord Read(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3) == 1, r.GetInt32(4) == 1,
        r.IsDBNull(5) ? null : r.GetInt32(5),
        r.IsDBNull(6) ? null : r.GetInt32(6),
        r.IsDBNull(7) ? null : r.GetInt32(7),
        r.IsDBNull(8) ? null : r.GetString(8),
        Sql.ParseUtc(r.GetString(9)));
}
