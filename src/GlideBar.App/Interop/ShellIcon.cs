using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GlideBar.App.Interop;

/// <summary>
/// Extracts a file / shortcut / app's shell icon as a frozen <see cref="BitmapSource"/>.
/// The result is frozen so it can be produced on a background thread (icon enumeration for the
/// app picker) and bound on the UI thread. Never throws — returns null if the icon is unavailable.
/// </summary>
internal static class ShellIcon
{
    public static BitmapSource? ForPath(string path, bool large = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var info = new NativeMethods.SHFILEINFO();
        uint flags = NativeMethods.SHGFI_ICON |
                     (large ? NativeMethods.SHGFI_LARGEICON : NativeMethods.SHGFI_SMALLICON);

        var res = NativeMethods.SHGetFileInfo(path, 0, ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);

        if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { NativeMethods.DestroyIcon(info.hIcon); }
    }
}
