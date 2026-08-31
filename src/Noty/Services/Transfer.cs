using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using Noty.Core;

namespace Noty.Services;

// MARK: - Archive format

/// The same JSON the macOS build writes, so a `.stickies` archive moves between
/// the two platforms untouched.
public sealed class StickyArchive
{
    public int Version { get; set; } = 1;
    public string App { get; set; } = "Noty";
    public DateTime Exported { get; set; } = DateTime.Now;
    public List<StickyNote> Notes { get; set; } = new();
}

public sealed class StickyNote
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int Color { get; set; }
    public string ColorName { get; set; } = "";
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public bool Archived { get; set; }
    public double Order { get; set; }

    public StickyNote() { }

    public StickyNote(Note n)
    {
        Id = n.Id;
        Title = n.Title;
        Body = n.Body;
        Color = n.Color;
        ColorName = n.Palette.Name;
        Created = n.Created;
        Modified = n.Modified;
        Archived = n.Archived;
        Order = n.Order;
    }

    public Note ToNote() => new()
    {
        Id = Id,
        Title = string.IsNullOrEmpty(Title) ? Note.DerivedTitle(Body) : Title,
        Body = Body,
        Color = Color,
        Created = Created,
        Modified = Modified,
        Archived = Archived,
        Order = Order,
    };
}

// MARK: - Export / import

public static class Transfer
{
    public enum Format { Markdown, PlainText, SingleFile, Stickies }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static void Export(Format format, IReadOnlyList<Note> notes)
    {
        if (notes.Count == 0)
        {
            Alert("Nothing to export", "There are no notes yet.");
            return;
        }
        switch (format)
        {
            case Format.Markdown: PerFile(notes, "md", MarkdownBody); break;
            case Format.PlainText: PerFile(notes, "txt", n => n.Body); break;
            case Format.SingleFile: SingleFile(notes); break;
            case Format.Stickies: Archive(notes); break;
        }
    }

    // One file per note, into a folder the user picks.
    private static void PerFile(IReadOnlyList<Note> notes, string ext, Func<Note, string> render)
    {
        var picker = new OpenFolderDialog
        {
            Title = $"Choose a folder for {notes.Count} {ext.ToUpperInvariant()} " +
                    $"file{(notes.Count == 1 ? "" : "s")}",
        };
        if (picker.ShowDialog() != true) return;
        var dir = picker.FolderName;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = 0;
        foreach (var n in notes)
        {
            var name = SafeName(n);
            var candidate = name;
            var i = 2;
            while (!used.Add(candidate)) candidate = $"{name}-{i++}";
            try
            {
                File.WriteAllText(Path.Combine(dir, $"{candidate}.{ext}"), render(n), Encoding.UTF8);
                written++;
            }
            catch (Exception e)
            {
                Log.Line($"export failed for {candidate}.{ext}: {e.Message}");
            }
        }
        Reveal(dir);
        if (written < notes.Count)
            Alert("Export incomplete", $"Wrote {written} of {notes.Count} notes. See noty.log for details.");
    }

    private static void SingleFile(IReadOnlyList<Note> notes)
    {
        var panel = new SaveFileDialog
        {
            FileName = $"Noty-{Fmt.FileStamp(DateTime.Now)}.md",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (panel.ShowDialog() != true) return;

        var doc = string.Join("\n\n---\n\n", notes.Select(n =>
            $"## {n.DisplayTitle}\n" +
            $"<!-- {n.Palette.Name} · created {Fmt.Stamp(n.Created)} · " +
            $"modified {Fmt.Stamp(n.Modified)}{(n.Archived ? " · archived" : "")} -->\n\n" +
            TaskSyntax.ToMarkdown(n.Body)));

        var header = $"# Noty export\n\n{notes.Count} notes · {Fmt.Stamp(DateTime.Now)}\n\n---\n\n";
        Write(header + doc, panel.FileName);
    }

    private static void Archive(IReadOnlyList<Note> notes)
    {
        var panel = new SaveFileDialog
        {
            FileName = $"Noty-{Fmt.FileStamp(DateTime.Now)}.stickies",
            Filter = "Sticky archive (*.stickies)|*.stickies|All files (*.*)|*.*",
        };
        if (panel.ShowDialog() != true) return;

        var archive = new StickyArchive { Notes = notes.Select(n => new StickyNote(n)).ToList() };
        Write(JsonSerializer.Serialize(archive, Json), panel.FileName);
    }

    public static void Import()
    {
        var panel = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Noty archives and notes|*.stickies;*.md;*.txt|" +
                     "Sticky archive (*.stickies)|*.stickies|Text and Markdown|*.md;*.txt",
        };
        if (panel.ShowDialog() != true) return;

        var incoming = new List<Note>();
        foreach (var path in panel.FileNames)
        {
            try
            {
                var text = File.ReadAllText(path);
                if (Path.GetExtension(path).Equals(".stickies", StringComparison.OrdinalIgnoreCase))
                {
                    var archive = JsonSerializer.Deserialize<StickyArchive>(text, Json);
                    if (archive is not null)
                        incoming.AddRange(archive.Notes.Select(s => s.ToNote()));
                }
                else
                {
                    var body = TaskSyntax.FromMarkdown(text);
                    incoming.Add(new Note
                    {
                        Body = body,
                        Title = Note.DerivedTitle(body),
                        Color = incoming.Count % NoteColor.All.Count,
                    });
                }
            }
            catch (Exception e)
            {
                Log.Line($"import failed for {path}: {e.Message}");
            }
        }

        var added = NoteStore.Shared.Ingest(incoming);
        Alert("Import finished", added == 0
            ? "Nothing could be read from those files."
            : $"Added {added} note{(added == 1 ? "" : "s")}.");
    }

    private static string MarkdownBody(Note n)
    {
        var header = $"# {n.DisplayTitle}\n\n" +
                     $"<!-- {n.Palette.Name} · created {Fmt.Stamp(n.Created)} · " +
                     $"modified {Fmt.Stamp(n.Modified)}{(n.Archived ? " · archived" : "")} -->\n\n";
        // The first line is already the title, so it is not repeated in the body.
        var lines = n.Body.Split('\n').Skip(1);
        return header + TaskSyntax.ToMarkdown(string.Join("\n", lines)).TrimStart('\n');
    }

    private static string SafeName(Note n)
    {
        var title = string.IsNullOrWhiteSpace(n.Title) ? "note" : n.Title;
        var clean = Regex.Replace(title, @"[\\/:*?""<>|\r\n\t]", " ").Trim();
        clean = Regex.Replace(clean, @"\s+", " ");
        if (clean.Length > 48) clean = clean[..48].Trim();
        if (clean.Length == 0) clean = "note";
        return $"{clean} {Fmt.FileStamp(n.Created)}";
    }

    private static void Write(string text, string path)
    {
        try
        {
            File.WriteAllText(path, text, Encoding.UTF8);
            Reveal(Path.GetDirectoryName(path) ?? path);
        }
        catch (Exception e)
        {
            Alert("Export failed", e.Message);
        }
    }

    private static void Reveal(string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { /* opening Explorer is a courtesy, not a requirement */ }
    }

    private static void Alert(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
