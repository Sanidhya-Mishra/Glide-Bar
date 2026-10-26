using GlideBar.HardwareMonitor.Interop;

namespace GlideBar.HardwareMonitor.Readers;

/// <summary>
/// System-wide CPU load (%) from <c>GetSystemTimes</c> deltas. Preferred over the
/// <c>% Processor Time</c> performance counter: a couple of kernel calls per tick,
/// no counter handles, steadier readings. Not thread-safe; owned by the sample loop.
/// </summary>
internal sealed class CpuUsageReader
{
    private ulong _prevIdle;
    private ulong _prevKernel; // kernel time INCLUDES idle time
    private ulong _prevUser;
    private bool _hasBaseline;

    /// <summary>Captures the first time snapshot so the next <see cref="Read"/> has a valid delta.</summary>
    public void Reset()
    {
        _hasBaseline = TryCapture(out _prevIdle, out _prevKernel, out _prevUser);
    }

    /// <summary>CPU busy percentage since the previous call (0 on the first call after a reset).</summary>
    public double Read()
    {
        if (!TryCapture(out ulong idle, out ulong kernel, out ulong user))
            return 0;

        if (!_hasBaseline)
        {
            _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
            _hasBaseline = true;
            return 0;
        }

        ulong idleDelta = idle - _prevIdle;
        ulong kernelDelta = kernel - _prevKernel;
        ulong userDelta = user - _prevUser;

        _prevIdle = idle; _prevKernel = kernel; _prevUser = user;

        ulong total = kernelDelta + userDelta; // kernel already includes idle
        if (total == 0)
            return 0;

        double busy = (double)(total - idleDelta) / total * 100.0;
        return busy < 0 ? 0 : busy > 100 ? 100 : busy;
    }

    private static bool TryCapture(out ulong idle, out ulong kernel, out ulong user)
    {
        if (NativeMethods.GetSystemTimes(out var i, out var k, out var u))
        {
            idle = i.ToUInt64(); kernel = k.ToUInt64(); user = u.ToUInt64();
            return true;
        }
        idle = kernel = user = 0;
        return false;
    }
}
