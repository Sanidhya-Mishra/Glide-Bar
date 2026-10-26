using GlideBar.HardwareMonitor.Interop;

namespace GlideBar.HardwareMonitor.Readers;

/// <summary>Physical RAM usage via <c>GlobalMemoryStatusEx</c>. Cheap, no admin, no handles.</summary>
internal sealed class MemoryReader
{
    public readonly record struct Reading(long UsedBytes, long TotalBytes, double UsedPercent);

    public Reading Read()
    {
        var status = new NativeMethods.MEMORYSTATUSEX
        {
            DwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
        };

        if (!NativeMethods.GlobalMemoryStatusEx(ref status) || status.UllTotalPhys == 0)
            return new Reading(0, 0, 0);

        long total = (long)status.UllTotalPhys;
        long used = (long)(status.UllTotalPhys - status.UllAvailPhys);
        // dwMemoryLoad is the OS's own used-percentage; trust it over recomputing.
        double percent = status.DwMemoryLoad;
        return new Reading(used, total, percent);
    }
}
