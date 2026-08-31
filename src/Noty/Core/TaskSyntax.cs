using System.Text.RegularExpressions;

namespace Noty.Core;

/// Checkbox tasks are stored inline in the note body as ☐ / ☑ line prefixes, so a
/// note is still plain text and exports cleanly to Markdown task syntax.
public static class TaskSyntax
{
    public const char Open = '☐';   // ☐
    public const char Done = '☑';   // ☑
    public const string OpenPrefix = "☐ ";
    public const string DonePrefix = "☑ ";

    public static char? Marker(ReadOnlySpan<char> line)
    {
        if (line.Length == 0) return null;
        var f = line[0];
        return f == Open || f == Done ? f : null;
    }

    public static bool IsTask(ReadOnlySpan<char> line) => Marker(line) is not null;

    /// Strip the marker for display in lists and titles.
    public static string Stripped(string line)
    {
        if (!IsTask(line)) return line;
        return line[1..].TrimStart();
    }

    private static readonly Regex MdOpen =
        new(@"^([ \t]*)[-*]\s+\[[ ]\]\s+", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MdDone =
        new(@"^([ \t]*)[-*]\s+\[[xX]\]\s+", RegexOptions.Multiline | RegexOptions.Compiled);

    /// Markdown task syntax in, ☐/☑ out.
    public static string FromMarkdown(string text) =>
        MdDone.Replace(MdOpen.Replace(text, "$1" + OpenPrefix), "$1" + DonePrefix);

    /// ☐/☑ out, Markdown task syntax in.
    public static string ToMarkdown(string text) =>
        text.Replace(OpenPrefix, "- [ ] ").Replace(DonePrefix, "- [x] ");
}
