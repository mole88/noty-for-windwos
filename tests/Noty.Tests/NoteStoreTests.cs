using Microsoft.Data.Sqlite;
using Noty.Core;
using NUnit.Framework;
using System.IO;

namespace Noty.Tests;

[NonParallelizable]
public sealed class NoteStoreTests
{
    private string _database = null!;
    private Store _databaseStore = null!;
    private NoteStore _notes = null!;

    [SetUp]
    public void SetUp()
    {
        _database = Path.Combine(TestEnvironment.Root, $"store-{Guid.NewGuid():N}.db");
        _databaseStore = new Store(_database);
        _notes = new NoteStore(_databaseStore);
    }

    [TearDown]
    public void TearDown()
    {
        _notes.Dispose();
        SqliteConnection.ClearAllPools();
        DeleteIfPresent(_database);
        DeleteIfPresent(_database + "-wal");
        DeleteIfPresent(_database + "-shm");
    }

    [Test]
    public void Create_derives_title_color_and_top_order()
    {
        var first = _notes.Create("# First", color: 3);
        var second = _notes.Create("☐ Second");

        Assert.Multiple(() =>
        {
            Assert.That(first.Title, Is.EqualTo("First"));
            Assert.That(first.Color, Is.EqualTo(3));
            Assert.That(second.Title, Is.EqualTo("Second"));
            Assert.That(second.Order, Is.LessThan(first.Order));
            Assert.That(_notes.Active.Select(n => n.Id), Is.EqualTo(new[] { second.Id, first.Id }));
        });
    }

    [Test]
    public void UpdateBody_updates_title_modified_date_and_notifies_once()
    {
        var note = _notes.Create("old");
        var before = note.Modified;
        var changes = 0;
        _notes.NotesChanged += (_, _) => changes++;

        _notes.UpdateBody(note.Id, "# New title\nbody");
        _notes.UpdateBody(note.Id, "# New title\nbody");

        Assert.Multiple(() =>
        {
            Assert.That(note.Body, Is.EqualTo("# New title\nbody"));
            Assert.That(note.Title, Is.EqualTo("New title"));
            Assert.That(note.Modified, Is.GreaterThanOrEqualTo(before));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void Pin_color_and_archive_mutations_are_persisted()
    {
        var note = _notes.Create("note", color: NoteColor.All.Count - 1);

        _notes.TogglePin(note.Id);
        _notes.CycleColor(note.Id);
        _notes.SetColor(note.Id, 5);
        _notes.SetArchived(note.Id, true);

        Assert.Multiple(() =>
        {
            Assert.That(note.Pinned, Is.True);
            Assert.That(note.Color, Is.EqualTo(5));
            Assert.That(note.Archived, Is.True);
            Assert.That(_notes.Active, Is.Empty);
            Assert.That(_notes.Archived.Single().Id, Is.EqualTo(note.Id));
        });

        using var reloaded = new Store(_database);
        var fromDisk = reloaded.Load().Single();
        Assert.Multiple(() =>
        {
            Assert.That(fromDisk.Pinned, Is.True);
            Assert.That(fromDisk.Color, Is.EqualTo(5));
            Assert.That(fromDisk.Archived, Is.True);
        });
    }

    [Test]
    public void Unarchive_places_a_note_at_the_top()
    {
        var first = _notes.Create("first");
        var second = _notes.Create("second");
        _notes.SetArchived(first.Id, true);

        _notes.SetArchived(first.Id, false);

        Assert.That(_notes.Active.Select(n => n.Id), Is.EqualTo(new[] { first.Id, second.Id }));
    }

    [Test]
    public void Reorder_moves_by_slots_and_rewrites_dense_order()
    {
        var a = _notes.Create("a");
        var b = _notes.Create("b");
        var c = _notes.Create("c");

        _notes.Reorder(c.Id, 2);

        Assert.Multiple(() =>
        {
            Assert.That(_notes.Active.Select(n => n.Id), Is.EqualTo(new[] { b.Id, a.Id, c.Id }));
            Assert.That(_notes.Active.Select(n => n.Order), Is.EqualTo(new[] { 0d, 1d, 2d }));
        });
    }

    [Test]
    public void Move_places_a_note_before_a_target_or_at_the_end()
    {
        var a = _notes.Create("a");
        var b = _notes.Create("b");
        var c = _notes.Create("c");

        _notes.Move(a.Id, c.Id);
        Assert.That(_notes.Active.IndexOf(a), Is.LessThan(_notes.Active.IndexOf(c)));

        _notes.Move(b.Id, null);
        Assert.That(_notes.Active.Last().Id, Is.EqualTo(b.Id));
    }

    [Test]
    public void DiscardIfEmpty_removes_only_blank_drafts()
    {
        var empty = _notes.Create("  \n");
        var written = _notes.Create("content");

        Assert.Multiple(() =>
        {
            Assert.That(_notes.DiscardIfEmpty(empty.Id), Is.True);
            Assert.That(_notes.DiscardIfEmpty(written.Id), Is.False);
            Assert.That(_notes.Get(empty.Id), Is.Null);
            Assert.That(_notes.Get(written.Id), Is.SameAs(written));
        });
    }

    [Test]
    public void Delete_can_be_undone_with_the_same_note()
    {
        var note = _notes.Create("recover me");

        _notes.Delete(note.Id);
        Assert.Multiple(() =>
        {
            Assert.That(_notes.Get(note.Id), Is.Null);
            Assert.That(_notes.PendingUndo?.Note, Is.SameAs(note));
        });

        _notes.UndoDelete();
        Assert.Multiple(() =>
        {
            Assert.That(_notes.Get(note.Id), Is.SameAs(note));
            Assert.That(_notes.PendingUndo, Is.Null);
        });
    }

    [Test]
    public void Ingest_replaces_duplicate_ids_and_assigns_order()
    {
        var existing = _notes.Create("existing");
        var duplicate = new Note { Id = existing.Id, Body = "imported" };
        var fresh = new Note { Id = "fresh", Body = "fresh" };

        var count = _notes.Ingest(new[] { duplicate, fresh });

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(2));
            Assert.That(duplicate.Id, Is.Not.EqualTo(existing.Id));
            Assert.That(_notes.Notes, Has.Count.EqualTo(3));
            Assert.That(duplicate.Order, Is.GreaterThan(fresh.Order));
        });
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
