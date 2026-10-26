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

        // Preferred: a high-resolution icon from the system image list. It's crisper than the 32px
        // SHGFI_ICON path and returns the base icon, so shortcuts (.lnk) come back without the
        // shell's shortcut-arrow overlay.
        if (large)
        {
            var hi = FromImageList(path);
            if (hi is not null) return hi;
        }

        return FromShGetFileInfo(path, large);
    }

    /// <summary>Pull the icon by index from the extra-large (48px) system image list.</summary>
    private static BitmapSource? FromImageList(string path)
    {
        var info = new NativeMethods.SHFILEINFO();
        var res = NativeMethods.SHGetFileInfo(path, 0, ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), NativeMethods.SHGFI_SYSICONINDEX);
        if (res == IntPtr.Zero) return null; // info.iIcon now holds the system image-list index

        var iid = NativeMethods.IID_IImageList;
        if (NativeMethods.SHGetImageList(NativeMethods.SHIL_EXTRALARGE, ref iid, out var himl) != 0
            || himl == IntPtr.Zero)
            return null;

        var hIcon = NativeMethods.ImageList_GetIcon(himl, info.iIcon, NativeMethods.ILD_TRANSPARENT);
        if (hIcon == IntPtr.Zero) return null;

        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { NativeMethods.DestroyIcon(hIcon); }
    }

    /// <summary>Classic fallback: a 32px (or 16px) icon via SHGFI_ICON.</summary>
    private static BitmapSource? FromShGetFileInfo(string path, bool large)
    {
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
