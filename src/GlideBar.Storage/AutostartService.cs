using System.Diagnostics;
using GlideBar.Core;
using GlideBar.Core.Abstractions;
using Microsoft.Win32;

namespace GlideBar.Storage;

/// <summary>
/// Toggles "start with Windows" via the per-user Run key
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. This needs no elevation and
/// is removed cleanly when disabled. The value points at the current executable.
/// </summary>
public sealed class AutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Registry value name under the Run key.</summary>
    private const string ValueName = AppPaths.AppName;

    /// <summary>True when the Run-key value exists and is non-empty.</summary>
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
            return;

        if (enabled)
        {
            var exe = GetExecutablePath();
            if (!string.IsNullOrEmpty(exe))
                key.SetValue(ValueName, Quote(exe), RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Best-effort resolution of the running executable's full path.</summary>
    private static string GetExecutablePath()
    {
        // Environment.ProcessPath is the real host exe on .NET 6+ (incl. single-file / apphost).
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
            return path;

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Wrap in quotes so a path containing spaces is launched as a single argument.</summary>
    private static string Quote(string path) =>
        path.StartsWith('"') ? path : $"\"{path}\"";
}
