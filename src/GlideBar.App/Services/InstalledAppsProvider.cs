using System.IO;
using Microsoft.Win32;

namespace GlideBar.App.Services;

/// <summary>One discovered installed app (name + a launchable path: a shortcut or an .exe).</summary>
public sealed record InstalledApp(string Name, string Path);

/// <summary>
/// Enumerates installed desktop apps from several sources so apps on ANY drive are found, not just
/// the C: Start Menu:
///   1. Start-menu shortcuts (common + per-user) — best display names.
///   2. Registry <c>App Paths</c> — registered executables (any drive).
///   3. Registry <c>Uninstall</c> entries — installed programs with a <c>DisplayIcon</c> exe (any drive).
/// Results are de-duplicated by name and sorted. COM-free and defensive — never throws into the UI.
/// </summary>
public sealed class InstalledAppsProvider
{
    public IReadOnlyList<InstalledApp> Enumerate()
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<InstalledApp>();

        void Add(string? name, string? path)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return;

            name = name.Trim();
            path = path.Trim().Trim('"');
            if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) return;
            if (!seenNames.Add(name)) return;

            apps.Add(new InstalledApp(name, path));
        }

        try { AddStartMenu(Add); } catch { }
        try { AddAppPaths(Add); } catch { }
        try { AddUninstall(Add); } catch { }

        return apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---- 1. Start-menu shortcuts ---------------------------------------------------------
    private static void AddStartMenu(Action<string?, string?> add)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var f in files)
            {
                try { add(Path.GetFileNameWithoutExtension(f), f); } catch { }
            }
        }
    }

    // ---- 2. App Paths (registered executables, any drive) --------------------------------
    private static void AddAppPaths(Action<string?, string?> add)
    {
        const string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
        foreach (var (hive, view) in BaseKeys())
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var appPaths = SafeOpen(baseKey, sub);
            if (appPaths is null) continue;

            foreach (var name in SafeSubKeyNames(appPaths))
            {
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                using var k = SafeOpen(appPaths, name);
                var path = k?.GetValue(null) as string;   // (Default) = full exe path
                if (string.IsNullOrWhiteSpace(path)) continue;
                path = path.Trim().Trim('"');
                if (!File.Exists(path)) continue;

                add(Path.GetFileNameWithoutExtension(path), path);
            }
        }
    }

    // ---- 3. Uninstall entries (installed programs, any drive) ----------------------------
    private static void AddUninstall(Action<string?, string?> add)
    {
        const string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var (hive, view) in BaseKeys())
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = SafeOpen(baseKey, sub);
            if (uninstall is null) continue;

            foreach (var name in SafeSubKeyNames(uninstall))
            {
                using var k = SafeOpen(uninstall, name);
                if (k is null) continue;

                var display = k.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(display)) continue;
                if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;

                var exe = ResolveLaunchExe(
                    k.GetValue("DisplayIcon") as string,
                    k.GetValue("InstallLocation") as string);
                if (exe is null) continue;

                add(display, exe);
            }
        }
    }

    /// <summary>Prefer a DisplayIcon exe; else a single exe found directly in InstallLocation.</summary>
    private static string? ResolveLaunchExe(string? displayIcon, string? installLocation)
    {
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            var p = displayIcon.Split(',')[0].Trim().Trim('"');
            if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                return p;
        }

        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            var dir = installLocation.Trim().Trim('"');
            if (Directory.Exists(dir))
            {
                try
                {
                    var exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                        .Where(e => !Path.GetFileName(e).Contains("unins", StringComparison.OrdinalIgnoreCase))
                        .Take(2).ToList();
                    if (exes.Count == 1) return exes[0];
                }
                catch { }
            }
        }

        return null;
    }

    // ---- Registry helpers ----------------------------------------------------------------
    private static IEnumerable<(RegistryHive hive, RegistryView view)> BaseKeys()
    {
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry64);
    }

    private static RegistryKey? SafeOpen(RegistryKey parent, string sub)
    {
        try { return parent.OpenSubKey(sub); } catch { return null; }
    }

    private static string[] SafeSubKeyNames(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); } catch { return Array.Empty<string>(); }
    }
}
