using FileSort.Core.Models;
using Microsoft.Data.Sqlite;

namespace FileSort.Data;

public sealed class CategoryStore
{
    private readonly Database _db;
    public CategoryStore(Database db) => _db = db;

    public IReadOnlyList<Category> ListCategories()
    {
        using var c = _db.Open();
        using var cmd = c.Cmd("SELECT Id, Name, ColorHex, SortOrder FROM Categories ORDER BY SortOrder, Id");
        using var r = cmd.ExecuteReader();
        var list = new List<Category>();
        while (r.Read()) list.Add(new Category(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
        return list;
    }

    public IReadOnlyList<Priority> ListPriorities()
    {
        using var c = _db.Open();
        using var cmd = c.Cmd("SELECT Id, Level, Key FROM Priorities ORDER BY Level");
        using var r = cmd.ExecuteReader();
        var list = new List<Priority>();
        while (r.Read()) list.Add(new Priority(r.GetInt32(0), r.GetInt32(1), r.GetString(2)));
        return list;
    }

    public Category AddCategory(string name, string colorHex, bool isDefault = false)
    {
        try
        {
            using var c = _db.Open();
            using (var ins = c.Cmd(
                "INSERT INTO Categories(Name,ColorHex,SortOrder,IsDefault,CreatedUtc) VALUES(@n,@c,(SELECT COALESCE(MAX(SortOrder),0)+1 FROM Categories),@d,@t)",
                null, ("@n", name), ("@c", colorHex), ("@d", isDefault ? 1 : 0), ("@t", Sql.Now())))
                ins.ExecuteNonQuery();
            long id;
            using (var idCmd = c.Cmd("SELECT last_insert_rowid()")) id = Convert.ToInt64(idCmd.ExecuteScalar());
            using var get = c.Cmd("SELECT Id, Name, ColorHex, SortOrder FROM Categories WHERE Id=@i", null, ("@i", id));
            using var r = get.ExecuteReader();
            r.Read();
            return new Category(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3));
        }
        catch (SqliteException ex) when (Sql.IsConstraint(ex))
        {
            throw new DataConflictException(DataConflictKind.DuplicateName, ex);
        }
    }

    public bool UpdateCategory(int id, string name, string colorHex)
    {
        try
        {
            using var c = _db.Open();
            using var cmd = c.Cmd("UPDATE Categories SET Name=@n, ColorHex=@c WHERE Id=@i", null, ("@n", name), ("@c", colorHex), ("@i", id));
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (SqliteException ex) when (Sql.IsConstraint(ex))
        {
            throw new DataConflictException(DataConflictKind.DuplicateName, ex);
        }
    }

    public bool CategoryExists(int id)
    {
        using var c = _db.Open();
        using var cmd = c.Cmd("SELECT COUNT(*) FROM Categories WHERE Id=@i", null, ("@i", id));
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public int CountClassified(int categoryId)
    {
        using var c = _db.Open();
        using var cmd = c.Cmd("SELECT COUNT(*) FROM FileClassifications WHERE CategoryId=@i", null, ("@i", categoryId));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Deletes a category. If files are linked to it, the caller MUST pick a strategy
    /// (reassign to another category, or clear the classification); otherwise InvalidOperationException.
    /// Files on disk are never touched. Returns how many classification records were affected.
    /// </summary>
    public int DeleteCategory(int id, int? reassignTo, bool clearClassifications)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        int linked;
        using (var cnt = c.Cmd("SELECT COUNT(*) FROM FileClassifications WHERE CategoryId=@i", tx, ("@i", id)))
            linked = Convert.ToInt32(cnt.ExecuteScalar());

        if (linked > 0)
        {
            if (reassignTo is int to)
            {
                if (to == id) throw new ArgumentException("Cannot reassign to the same category.");
                using (var ex = c.Cmd("SELECT COUNT(*) FROM Categories WHERE Id=@i", tx, ("@i", to)))
                    if (Convert.ToInt32(ex.ExecuteScalar()) == 0) throw new ArgumentException("Target category does not exist.");
                using var up = c.Cmd("UPDATE FileClassifications SET CategoryId=@to, UpdatedUtc=@t WHERE CategoryId=@i", tx,
                    ("@to", to), ("@t", Sql.Now()), ("@i", id));
                up.ExecuteNonQuery();
            }
            else if (clearClassifications)
            {
                using (var d1 = c.Cmd("DELETE FROM FileClassifications WHERE CategoryId=@i AND PriorityId IS NULL AND Note IS NULL", tx, ("@i", id)))
                    d1.ExecuteNonQuery();
                using (var u1 = c.Cmd("UPDATE FileClassifications SET CategoryId=NULL, UpdatedUtc=@t WHERE CategoryId=@i", tx, ("@t", Sql.Now()), ("@i", id)))
                    u1.ExecuteNonQuery();
                using (var d2 = c.Cmd("DELETE FROM Files WHERE Id NOT IN (SELECT FileId FROM FileClassifications)", tx))
                    d2.ExecuteNonQuery();
            }
            else
            {
                throw new InvalidOperationException("Category is in use; choose reassign or clear.");
            }
        }

        using (var del = c.Cmd("DELETE FROM Categories WHERE Id=@i", tx, ("@i", id)))
            del.ExecuteNonQuery();
        tx.Commit();
        return linked;
    }

    public void SeedDefaults(IEnumerable<DefaultCategory> defaults)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        foreach (var d in defaults)
        {
            using var cmd = c.Cmd(
                "INSERT OR IGNORE INTO Categories(Name,ColorHex,SortOrder,IsDefault,CreatedUtc) VALUES(@n,@c,(SELECT COALESCE(MAX(SortOrder),0)+1 FROM Categories),1,@t)",
                tx, ("@n", d.Name), ("@c", d.ColorHex), ("@t", Sql.Now()));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
