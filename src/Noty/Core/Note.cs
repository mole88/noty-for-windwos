using System.Text.RegularExpressions;

namespace Noty.Core;

public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int Color { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;
    public bool Archived { get; set; }
    public bool Pinned { get; set; }
    public double Order { get; set; }

    public NoteColor Palette => NoteColor.At(Color);

    public string DisplayTitle => string.IsNullOrEmpty(Title) ? "New note" : Title;

    public Note Copy() => (Note)MemberwiseClone();

    private static readonly Regex Heading = new(@"^#{1,6}\s*", RegexOptions.Compiled);

    /// Title shown in the fan / lists, derived from the first non-empty line.
    public static string DerivedTitle(string body)
    {
        var line = body.Split('\n', '\r').FirstOrDefault(l => true) ?? "";
        var clean = Heading.Replace(line.Trim(), "");
        clean = TaskSyntax.Stripped(clean).Trim();
        if (clean.Length == 0) return "";
        return clean.Length > 60 ? clean[..60] + "…" : clean;
    }

    /// Completed / total, or null when the note holds no tasks.
    public (int Done, int Total)? TaskProgress
    {
        get
        {
            int done = 0, total = 0;
            foreach (var line in Body.Split('\n'))
            {
                switch (TaskSyntax.Marker(line.TrimEnd('\r')))
                {
                    case TaskSyntax.Done: done++; total++; break;
                    case TaskSyntax.Open: total++; break;
                }
            }
            return total > 0 ? (done, total) : null;
        }
    }

    /// Second line onwards, collapsed — used as list subtitle.
    public string Preview
    {
        get
        {
            var lines = Body.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            var rest = string.Join(" ", lines.Skip(1)).Trim();
            return rest.Length > 120 ? rest[..120] + "…" : rest;
        }
    }
}
