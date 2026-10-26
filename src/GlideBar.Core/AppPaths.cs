namespace GlideBar.Core;

/// <summary>Well-known filesystem locations for GlideBar.</summary>
public static class AppPaths
{
    public const string AppName = "GlideBar";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
