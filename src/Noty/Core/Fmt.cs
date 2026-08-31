namespace Noty.Core;

public static class Fmt
{
    public static string Stamp(DateTime d) => d.ToString("d MMM yyyy, HH:mm");
    public static string FileStamp(DateTime d) => d.ToString("yyyy-MM-dd-HHmmss");

    public static string Ago(DateTime d)
    {
        var span = DateTime.Now - d;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} d ago";
        return Stamp(d);
    }
}
