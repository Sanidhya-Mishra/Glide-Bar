using System.Diagnostics;
using GlideBar.HardwareMonitor.Interop;

namespace GlideBar.HardwareMonitor.Readers;

/// <summary>
/// Tier-A GPU metrics from Windows performance counters (Win10 1709+), no admin:
///  - load %  : sum of <c>GPU Engine \ Utilization Percentage</c> across engine instances
///  - mem used: sum of <c>GPU Adapter Memory \ Dedicated Usage</c> across adapters
///  - mem total: cached once from DXGI (perf counters expose usage only)
///
/// Uses <see cref="PerformanceCounterCategory.ReadCategory"/> — ONE batched snapshot per tick
/// for all dynamic engine instances — instead of a PerformanceCounter object per instance,
/// which would allocate heavily and churn handles as processes come and go.
/// Not thread-safe; owned by the sample loop.
/// </summary>
internal sealed class GpuCountersReader
{
    private const string EngineCategory = "GPU Engine";
    private const string EngineCounter = "Utilization Percentage";
    private const string AdapterMemoryCategory = "GPU Adapter Memory";
    private const string DedicatedUsageCounter = "Dedicated Usage";

    private readonly PerformanceCounterCategory? _engine;
    private readonly PerformanceCounterCategory? _adapterMemory;

    // Previous per-instance samples for the utilization timer counter (needs a delta).
    private Dictionary<string, CounterSample> _prevUtil = new(StringComparer.Ordinal);
    private Dictionary<string, CounterSample> _currUtil = new(StringComparer.Ordinal);

    private readonly long _totalDedicatedVram;

    public bool LoadAvailable { get; }
    public bool MemoryAvailable { get; }

    /// <summary>Total dedicated VRAM in bytes (from DXGI), or 0 if unknown.</summary>
    public long TotalDedicatedVramBytes => _totalDedicatedVram;

    public GpuCountersReader()
    {
        try
        {
            if (PerformanceCounterCategory.Exists(EngineCategory))
            {
                _engine = new PerformanceCounterCategory(EngineCategory);
                LoadAvailable = true;
            }
        }
        catch { LoadAvailable = false; }

        try
        {
            if (PerformanceCounterCategory.Exists(AdapterMemoryCategory))
            {
                _adapterMemory = new PerformanceCounterCategory(AdapterMemoryCategory);
                MemoryAvailable = true;
            }
        }
        catch { MemoryAvailable = false; }

        _totalDedicatedVram = NativeMethods.GetTotalDedicatedVideoMemoryBytes();
    }

    /// <summary>Prime the utilization baseline so the first <see cref="ReadLoadPercent"/> is valid.</summary>
    public void Reset()
    {
        _prevUtil.Clear();
        _currUtil.Clear();
        if (_engine is null) return;
        try
        {
            var data = _engine.ReadCategory();
            var util = data[EngineCounter];
            if (util is not null)
            {
                foreach (System.Collections.DictionaryEntry e in util)
                {
                    var id = (InstanceData)e.Value!;
                    _prevUtil[(string)e.Key] = id.Sample;
                }
            }
        }
        catch { /* leave baseline empty; first read returns 0 */ }
    }

    /// <summary>Sum of engine utilization across all instances, clamped to 0..100.</summary>
    public double ReadLoadPercent()
    {
        if (_engine is null) return 0;
        try
        {
            var data = _engine.ReadCategory();
            var util = data[EngineCounter];
            if (util is null) return 0;

            _currUtil.Clear();
            double sum = 0;
            foreach (System.Collections.DictionaryEntry e in util)
            {
                var name = (string)e.Key;
                var curr = ((InstanceData)e.Value!).Sample;
                _currUtil[name] = curr;
                if (_prevUtil.TryGetValue(name, out var prev))
                {
                    float v = CounterSample.Calculate(prev, curr);
                    if (v > 0) sum += v;
                }
            }

            // swap current -> previous for the next tick (reuse dictionaries)
            (_prevUtil, _currUtil) = (_currUtil, _prevUtil);

            return sum < 0 ? 0 : sum > 100 ? 100 : sum;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Sum of dedicated VRAM currently in use across adapters, in bytes.</summary>
    public long ReadDedicatedMemoryUsedBytes()
    {
        if (_adapterMemory is null) return 0;
        try
        {
            var data = _adapterMemory.ReadCategory();
            var dedicated = data[DedicatedUsageCounter];
            if (dedicated is null) return 0;

            long sum = 0;
            foreach (System.Collections.DictionaryEntry e in dedicated)
                sum += ((InstanceData)e.Value!).RawValue;
            return sum;
        }
        catch
        {
            return 0;
        }
    }
}
