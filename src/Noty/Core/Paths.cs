using System.IO;

namespace Noty.Core;

/// Everything Noty owns lives in one folder under %APPDATA%.
public static class Paths
{
    public static string Support { get; } = Init();

    private static string Init()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Noty");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Db => Path.Combine(Support, "notes.db");
    public static string Key => Path.Combine(Support, "note.key");
    public static string SettingsFile => Path.Combine(Support, "settings.json");
    public static string Log => Path.Combine(Support, "noty.log");
}
