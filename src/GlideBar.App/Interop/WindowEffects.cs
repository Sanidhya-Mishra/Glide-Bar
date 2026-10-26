using System.Windows.Media;
using static GlideBar.App.Interop.NativeMethods;

namespace GlideBar.App.Interop;

/// <summary>
/// Window-level OS tricks for the overlay: mark it a tool window (hidden from Alt+Tab/taskbar),
/// apply the DWM acrylic backdrop + rounded corners, and set the immersive dark-mode flag.
/// All calls degrade gracefully on older OS builds.
/// </summary>
internal static class WindowEffects
{
    /// <summary>Adds WS_EX_TOOLWINDOW so the overlay never appears in Alt+Tab or the taskbar.
    /// (We intentionally do NOT add WS_EX_NOACTIVATE — the search box needs keyboard focus.)</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    /// <summary>
    /// Enables (or disables) the acrylic blur-behind for a layered (AllowsTransparency) window via
    /// <c>SetWindowCompositionAttribute</c>. The DWM system-backdrop API can't be used here because
    /// it is incompatible with per-pixel-transparent WPF windows; the composition accent works.
    /// </summary>
    public static bool SetBlurBehind(IntPtr hwnd, bool enable, bool dark)
    {
        SetDarkMode(hwnd, dark);
        try
        {
            uint tint = dark ? 0x40201A2Bu : 0x40F2F3F7u; // AABBGGRR translucent tint over the blur
            var accent = new ACCENT_POLICY
            {
                AccentState = enable ? ACCENT_ENABLE_ACRYLICBLURBEHIND : 0,
                GradientColor = tint,
            };
            int size = System.Runtime.InteropServices.Marshal.SizeOf<ACCENT_POLICY>();
            IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(accent, ptr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr); }
        }
        catch { return false; }
    }

    public static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        int v = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
    }
}
