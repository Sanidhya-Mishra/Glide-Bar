using System.Windows;
using static GlideBar.App.Interop.NativeMethods;

namespace GlideBar.App.Interop;

/// <summary>A monitor's work area in physical (device) pixels plus whether it is primary.</summary>
internal sealed record MonitorInfo(int Index, Rect Bounds, Rect WorkArea, bool IsPrimary);

/// <summary>
/// Enumerates monitors (physical pixels) and offers geometry helpers for multi-monitor placement:
/// which monitor contains a point, nearest edge, and validation of persisted placement.
/// </summary>
internal static class MonitorHelper
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        int i = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT clip, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                var bounds = ToRect(mi.rcMonitor);
                var work = ToRect(mi.rcWork);
                bool primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                list.Add(new MonitorInfo(i++, bounds, work, primary));
            }
            return true;
        }, IntPtr.Zero);

        if (list.Count == 0)
        {
            // Fallback: single primary from system metrics (physical ≈ logical here).
            var w = SystemParameters.PrimaryScreenWidth;
            var h = SystemParameters.PrimaryScreenHeight;
            list.Add(new MonitorInfo(0, new Rect(0, 0, w, h), new Rect(0, 0, w, h), true));
        }
        return list;
    }

    public static MonitorInfo Primary(IReadOnlyList<MonitorInfo> monitors) =>
        monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

    /// <summary>Monitor whose bounds contain the point (physical px); falls back to the nearest.</summary>
    public static MonitorInfo FromPhysicalPoint(IReadOnlyList<MonitorInfo> monitors, Point p)
    {
        foreach (var m in monitors)
            if (m.Bounds.Contains(p)) return m;

        // nearest by center distance
        MonitorInfo best = monitors[0];
        double bestDist = double.MaxValue;
        foreach (var m in monitors)
        {
            var c = new Point(m.Bounds.X + m.Bounds.Width / 2, m.Bounds.Y + m.Bounds.Height / 2);
            var d = (c - p).LengthSquared;
            if (d < bestDist) { bestDist = d; best = m; }
        }
        return best;
    }

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
}
