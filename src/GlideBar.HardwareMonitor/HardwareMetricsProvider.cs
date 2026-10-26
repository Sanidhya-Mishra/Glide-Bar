using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;
using GlideBar.HardwareMonitor.Interop;
using GlideBar.HardwareMonitor.Readers;

namespace GlideBar.HardwareMonitor;

/// <summary>
/// Real <see cref="IMetricsProvider"/> for Windows, layered cheap-first:
///
///  Tier A (always on, no admin): CPU load via GetSystemTimes, RAM via GlobalMemoryStatusEx,
///  GPU load + GPU memory via performance counters.
///
///  Tier B (opt-in via <see cref="TryEnableTemperaturesAsync"/>): CPU/GPU temperatures via
///  LibreHardwareMonitorLib. GPU temp usually works in user mode; CPU temp generally needs
///  elevation. <see cref="Capabilities"/> reflects what is actually readable on this machine.
///
/// Sampling runs on a <see cref="System.Threading.Timer"/> (thread-pool callback). <see cref="Stop"/>
/// fully halts sampling and releases both the timer and the LHM Computer handle, so idle cost is ~0.
/// Readers/handles are reused across ticks to avoid per-tick allocation storms.
/// </summary>
public sealed class HardwareMetricsProvider : IMetricsProvider
{
    private readonly object _gate = new();      // guards Start/Stop/SetInterval/timer lifecycle
    private readonly object _tempGate = new();   // serializes all LHM access

    private readonly CpuUsageReader _cpu = new();
    private readonly MemoryReader _mem = new();
    private readonly GpuCountersReader _gpu = new();
    private readonly TemperatureReader _tempReader = new();

    private Timer? _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private volatile bool _running;
    private int _sampling;                       // re-entrancy guard for the tick callback
    private bool _temperaturesRequested;         // user opted into Tier B
    private bool _disposed;

    private volatile SystemSnapshot _current = SystemSnapshot.Empty;
    private volatile MetricsCapabilities _capabilities;

    public HardwareMetricsProvider()
    {
        // Tier A CPU/RAM are always available; GPU load depends on the perf-counter category.
        _capabilities = new MetricsCapabilities(
            CpuLoad: true,
            RamLoad: true,
            GpuLoad: _gpu.LoadAvailable,
            CpuTemp: false,
            GpuTemp: false);
    }

    public SystemSnapshot Current => _current;
    public MetricsCapabilities Capabilities => _capabilities;
    public bool IsRunning => _running;

    /// <summary>True when the current process is elevated (needed for CPU-core temperature).</summary>
    public bool IsElevated { get; } = NativeMethods.IsProcessElevated();

    public event EventHandler<SystemSnapshot>? SampleProduced;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _running) return;
            _running = true;

            // Prime deltas so the first tick produces valid CPU/GPU-load values.
            _cpu.Reset();
            _gpu.Reset();

            _timer = new Timer(OnTick, null, _interval, _interval);
        }

        // Re-open the sensor library if the user had enabled temperatures before a Stop.
        if (_temperaturesRequested)
        {
            lock (_tempGate)
            {
                _tempReader.Open();
                ApplyTempCapabilities();
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            _timer?.Dispose();
            _timer = null;
        }

        // Release the LHM Computer handle (and its ring-0 driver, if loaded) so idle cost is ~0.
        lock (_tempGate)
        {
            _tempReader.Close();
        }
    }

    public void SetInterval(TimeSpan interval)
    {
        _interval = interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : interval;
        lock (_gate)
        {
            if (_running)
                _timer?.Change(_interval, _interval);
        }
    }

    public Task<bool> TryEnableTemperaturesAsync(CancellationToken ct = default)
    {
        if (_disposed) return Task.FromResult(false);
        _temperaturesRequested = true;

        // LHM Open() can take a few hundred ms (driver load / GPU init); do it off the caller's thread.
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            lock (_tempGate)
            {
                bool ok = _tempReader.Open();
                ApplyTempCapabilities();
                return ok;
            }
        }, ct);
    }

    private void OnTick(object? state)
    {
        if (!_running) return;
        if (Interlocked.CompareExchange(ref _sampling, 1, 0) != 0) return; // skip if a tick overruns

        try
        {
            if (!_running) return;

            double cpuLoad = _cpu.Read();
            double gpuLoad = _gpu.ReadLoadPercent();
            long gpuMemUsed = _gpu.ReadDedicatedMemoryUsedBytes();
            var mem = _mem.Read();

            double? cpuTemp = null, gpuTemp = null;
            if (_temperaturesRequested)
            {
                lock (_tempGate)
                {
                    (cpuTemp, gpuTemp) = _tempReader.Read();
                }
            }

            var snap = new SystemSnapshot
            {
                CpuLoadPercent = cpuLoad,
                GpuLoadPercent = gpuLoad,
                RamUsedPercent = mem.UsedPercent,
                RamUsedBytes = mem.UsedBytes,
                RamTotalBytes = mem.TotalBytes,
                GpuMemUsedBytes = gpuMemUsed,
                GpuMemTotalBytes = _gpu.TotalDedicatedVramBytes,
                CpuTempC = cpuTemp,
                GpuTempC = gpuTemp,
                TimestampUtc = DateTimeOffset.UtcNow,
            };

            _current = snap;
            if (_running)
                SampleProduced?.Invoke(this, snap);
        }
        catch
        {
            // A single bad tick must never tear down the timer.
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    private void ApplyTempCapabilities()
    {
        var c = _capabilities;
        _capabilities = c with
        {
            CpuTemp = _tempReader.CpuTempAvailable,
            GpuTemp = _tempReader.GpuTempAvailable,
        };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }
}
