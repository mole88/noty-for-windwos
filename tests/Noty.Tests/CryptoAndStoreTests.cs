using Microsoft.Data.Sqlite;
using Noty.Core;
using NUnit.Framework;
using System.IO;

namespace Noty.Tests;

[NonParallelizable]
public sealed class CryptoAndStoreTests
{
    [SetUp]
    public void ResetDatabase()
    {
        SqliteConnection.ClearAllPools();
        DeleteIfPresent(Paths.Db);
        DeleteIfPresent(Paths.Db + "-wal");
        DeleteIfPresent(Paths.Db + "-shm");
    }

    [Test]
    public void Crypto_round_trips_unicode_and_uses_a_fresh_nonce()
    {
        const string text = "Привет 👋\nsecret note";

        var first = Crypto.Seal(text);
        var second = Crypto.Seal(text);

        Assert.Multiple(() =>
        {
            Assert.That(Crypto.Open(first), Is.EqualTo(text));
            Assert.That(Crypto.Open(second), Is.EqualTo(text));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(first, Is.Not.EqualTo(System.Text.Encoding.UTF8.GetBytes(text)));
        });
    }

    [Test]
    public void Crypto_rejects_missing_short_and_tampered_payloads()
    {
        var tampered = Crypto.Seal("do not alter");
        tampered[^1] ^= 0x01;

        Assert.Multiple(() =>
        {
            Assert.That(Crypto.Open(null), Is.Empty);
            Assert.That(Crypto.Open(new byte[8]), Is.Empty);
            Assert.That(Crypto.Open(tampered), Is.Empty);
        });
    }

    [Test]
    public void Store_persists_updates_and_deletes_notes()
    {
        var created = DateTime.Now.AddDays(-2);
        var modified = DateTime.Now.AddMinutes(-3);
        var note = new Note
        {
            Id = "persisted-note",
            Title = "Private",
            Body = "Приватный текст",
            Color = 4,
            Created = created,
            Modified = modified,
            Archived = true,
            Pinned = true,
            Order = 12.5,
        };

        using (var store = new Store()) store.Upsert(note);

        Note loaded;
        using (var store = new Store()) loaded = store.Load().Single();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Id, Is.EqualTo(note.Id));
            Assert.That(loaded.Title, Is.EqualTo(note.Title));
            Assert.That(loaded.Body, Is.EqualTo(note.Body));
            Assert.That(loaded.Color, Is.EqualTo(note.Color));
            Assert.That(loaded.Archived, Is.True);
            Assert.That(loaded.Pinned, Is.True);
            Assert.That(loaded.Order, Is.EqualTo(note.Order));
            Assert.That(loaded.Created, Is.EqualTo(created).Within(TimeSpan.FromMilliseconds(2)));
            Assert.That(loaded.Modified, Is.EqualTo(modified).Within(TimeSpan.FromMilliseconds(2)));
        });

        note.Body = "updated";
        note.Title = "Updated";
        using (var store = new Store()) store.Upsert(note);
        using (var store = new Store())
        {
            Assert.That(store.Load().Single().Body, Is.EqualTo("updated"));
            store.Delete(note.Id);
            Assert.That(store.Load(), Is.Empty);
        }
    }

    [Test]
    public void Store_keeps_note_bodies_encrypted_in_SQLite()
    {
        const string secret = "text-that-must-not-be-plain";
        using (var store = new Store())
            store.Upsert(new Note { Id = "encrypted", Title = "Visible", Body = secret });

        using var db = new SqliteConnection($"Data Source={Paths.Db};Mode=ReadOnly");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT body FROM notes WHERE id='encrypted'";
        var blob = (byte[])command.ExecuteScalar()!;

        Assert.Multiple(() =>
        {
            Assert.That(blob, Has.Length.GreaterThan(secret.Length));
            Assert.That(System.Text.Encoding.UTF8.GetString(blob), Does.Not.Contain(secret));
        });
    }

    [Test]
    public void Store_migrates_a_database_without_the_pinned_column()
    {
        using (var db = new SqliteConnection($"Data Source={Paths.Db}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE notes (
                  id TEXT PRIMARY KEY,
                  title TEXT NOT NULL DEFAULT '',
                  body BLOB NOT NULL,
                  color INTEGER NOT NULL DEFAULT 0,
                  created REAL NOT NULL,
                  modified REAL NOT NULL,
                  archived INTEGER NOT NULL DEFAULT 0,
                  sort_order REAL NOT NULL DEFAULT 0
                );
                """;
            command.ExecuteNonQuery();
        }

        using var store = new Store();
        using var migrated = new SqliteConnection($"Data Source={Paths.Db};Mode=ReadOnly");
        migrated.Open();
        using var schema = migrated.CreateCommand();
        schema.CommandText = "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name='pinned'";

        Assert.That(Convert.ToInt32(schema.ExecuteScalar()), Is.EqualTo(1));
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
