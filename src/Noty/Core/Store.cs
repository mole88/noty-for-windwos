using Microsoft.Data.Sqlite;

namespace Noty.Core;

/// SQLite-backed note storage. Bodies are AES-GCM sealed; title/colour/dates stay
/// plaintext so lists can render without unsealing every row.
public sealed class Store : IDisposable
{
    private readonly SqliteConnection _db;

    public Store()
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Paths.Db,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        _db.Open();

        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA synchronous=NORMAL;");
        Exec("""
        CREATE TABLE IF NOT EXISTS notes (
          id TEXT PRIMARY KEY,
          title TEXT NOT NULL DEFAULT '',
          body BLOB NOT NULL,
          color INTEGER NOT NULL DEFAULT 0,
          created REAL NOT NULL,
          modified REAL NOT NULL,
          archived INTEGER NOT NULL DEFAULT 0,
          sort_order REAL NOT NULL DEFAULT 0
        );
        """);
        Exec("CREATE INDEX IF NOT EXISTS idx_notes_archived ON notes(archived, sort_order);");
        Migrate();
    }

    /// Adds columns introduced after a database was first created. Checked rather
    /// than attempted-and-ignored, so a real failure still shows up in the log.
    private void Migrate()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(notes);";
            using var r = cmd.ExecuteReader();
            while (r.Read()) existing.Add(r.GetString(1));
        }
        if (!existing.Contains("pinned"))
        {
            Exec("ALTER TABLE notes ADD COLUMN pinned INTEGER NOT NULL DEFAULT 0;");
            Log.Line("migrated notes table — added pinned");
        }
    }

    private void Exec(string sql)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        catch (Exception e)
        {
            Log.Line($"sql: {e.Message}");
        }
    }

    // MARK: Reads

    public List<Note> Load()
    {
        var outp = new List<Note>();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                "SELECT id,title,body,color,created,modified,archived,sort_order,pinned " +
                "FROM notes ORDER BY sort_order ASC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var blob = r.IsDBNull(2) ? null : (byte[])r["body"];
                outp.Add(new Note
                {
                    Id = r.GetString(0),
                    Title = r.IsDBNull(1) ? "" : r.GetString(1),
                    Body = Crypto.Open(blob),
                    Color = r.GetInt32(3),
                    Created = FromEpoch(r.GetDouble(4)),
                    Modified = FromEpoch(r.GetDouble(5)),
                    Archived = r.GetInt32(6) != 0,
                    Order = r.GetDouble(7),
                    Pinned = r.GetInt32(8) != 0,
                });
            }
        }
        catch (Exception e)
        {
            Log.Line($"load failed — {e.Message}");
        }
        return outp;
    }

    // MARK: Writes

    public void Upsert(Note n)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
            INSERT INTO notes (id,title,body,color,created,modified,archived,sort_order,pinned)
            VALUES ($id,$title,$body,$color,$created,$modified,$archived,$order,$pinned)
            ON CONFLICT(id) DO UPDATE SET
              title=excluded.title, body=excluded.body, color=excluded.color,
              modified=excluded.modified, archived=excluded.archived,
              sort_order=excluded.sort_order, pinned=excluded.pinned;
            """;
            cmd.Parameters.AddWithValue("$id", n.Id);
            cmd.Parameters.AddWithValue("$title", n.Title ?? "");
            cmd.Parameters.AddWithValue("$body", Crypto.Seal(n.Body ?? ""));
            cmd.Parameters.AddWithValue("$color", n.Color);
            cmd.Parameters.AddWithValue("$created", ToEpoch(n.Created));
            cmd.Parameters.AddWithValue("$modified", ToEpoch(n.Modified));
            cmd.Parameters.AddWithValue("$archived", n.Archived ? 1 : 0);
            cmd.Parameters.AddWithValue("$order", n.Order);
            cmd.Parameters.AddWithValue("$pinned", n.Pinned ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        catch (Exception e)
        {
            Log.Line($"upsert failed — {e.Message}");
        }
    }

    public void Delete(string id)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM notes WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch (Exception e)
        {
            Log.Line($"delete failed — {e.Message}");
        }
    }

    private static double ToEpoch(DateTime d) =>
        (d.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;

    private static DateTime FromEpoch(double s) =>
        DateTime.UnixEpoch.AddSeconds(s).ToLocalTime();

    public void Dispose() => _db.Dispose();
}
