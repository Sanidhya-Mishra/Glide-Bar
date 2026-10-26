using System.Diagnostics;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.Search;

/// <summary>
/// Real <see cref="ILauncher"/>: opens, reveals and launches items through the Windows shell.
/// Every operation is wrapped in try/catch so a bad path or a missing target can never throw into
/// the UI. Supports launching UWP/Store apps via <c>shell:AppsFolder\&lt;AUMID&gt;</c>.
/// </summary>
public sealed class ShellLauncher : ILauncher
{
    public void Open(SearchResult result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Path)) return;
        OpenPath(result.Path);
    }

    public void Open(PinnedItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Path)) return;

        // UWP/Store apps are launched by AppUserModelID, not a filesystem path.
        if (item.Kind == PinnedKind.App && LooksLikeAppUserModelId(item.Path))
        {
            LaunchAppsFolder(item.Path);
            return;
        }

        Launch(item.Path, item.Arguments);
    }

    public void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            LaunchAppsFolderRaw(path);
            return;
        }

        Launch(path, null);
    }

    public void RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            // /select, highlights the item inside its containing folder.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch { /* never throw into UI */ }
    }

    public void OpenContainingFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            // If the path is itself a directory, open it directly; otherwise open its parent.
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return;

            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch { }
    }

    private static void Launch(string target, string? arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(arguments))
                psi.Arguments = arguments;
            Process.Start(psi);
        }
        catch { }
    }

    private static void LaunchAppsFolder(string aumid)
        => LaunchAppsFolderRaw($"shell:AppsFolder\\{aumid}");

    private static void LaunchAppsFolderRaw(string shellTarget)
    {
        try
        {
            // explorer.exe resolves shell: monikers (including AppsFolder) reliably.
            Process.Start(new ProcessStartInfo("explorer.exe", shellTarget)
            {
                UseShellExecute = true,
            });
        }
        catch { }
    }

    /// <summary>
    /// Heuristic: an AUMID is not an existing file-system path and typically carries a '!' separating
    /// the package family name from the app id (e.g. <c>Microsoft.WindowsCalculator_8wekyb3d8bbwe!App</c>).
    /// </summary>
    private static bool LooksLikeAppUserModelId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (File.Exists(value) || Directory.Exists(value)) return false;
        if (value.Contains('!')) return true;
        // A bare token with no path separators and no drive/extension is treated as an AUMID.
        return !value.Contains('\\') && !value.Contains('/') && !Path.IsPathRooted(value);
    }
}
