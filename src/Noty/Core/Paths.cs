using System.IO;

namespace Noty.Core;

/// Everything Noty owns lives in one folder under %APPDATA%.
public static class Paths
{
    public static string Support { get; } = Init();

    private static string Init()
    {
        var overridden = Environment.GetEnvironmentVariable("NOTY_DATA_DIR");
        var dir = string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Noty")
            : Path.GetFullPath(overridden);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Db => Path.Combine(Support, "notes.db");
    public static string Key => Path.Combine(Support, "note.key");
    public static string SettingsFile => Path.Combine(Support, "settings.json");
    public static string Log => Path.Combine(Support, "noty.log");
}
