using LibreHardwareMonitor.Hardware;

namespace GlideBar.HardwareMonitor.Readers;

/// <summary>
/// Tier-B opt-in temperatures via LibreHardwareMonitorLib.
///
/// GPU core temperature usually reads in user mode (NVAPI / ADL / Intel). CPU core/package
/// temperature generally needs the kernel ring-0 driver, which requires elevation; without it
/// LHM exposes the CPU hardware but its temperature sensors read null. This class therefore
/// detects what is *actually readable* and reports that, never assuming a sensor exists.
///
/// The <see cref="Computer"/> handle is opened lazily and fully released by <see cref="Close"/>
/// (called from the provider's Stop) so idle cost is zero. Not thread-safe; owned by the sample loop.
/// </summary>
internal sealed class TemperatureReader
{
    private Computer? _computer;
    private readonly List<IHardware> _cpuHardware = new();
    private readonly List<IHardware> _gpuHardware = new();
    private ISensor? _cpuTempSensor;
    private ISensor? _gpuTempSensor;

    public bool IsOpen => _computer is not null;
    public bool CpuTempAvailable { get; private set; }
    public bool GpuTempAvailable { get; private set; }

    /// <summary>
    /// Opens the sensor library and locates the best CPU/GPU temperature sensors.
    /// Returns true if at least one temperature became readable. Never throws.
    /// </summary>
    public bool Open()
    {
        if (_computer is not null)
            return CpuTempAvailable || GpuTempAvailable;

        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
            };
            computer.Open();

            _cpuHardware.Clear();
            _gpuHardware.Clear();
            foreach (var hw in computer.Hardware)
            {
                switch (hw.HardwareType)
                {
                    case HardwareType.Cpu:
                        _cpuHardware.Add(hw);
                        break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        _gpuHardware.Add(hw);
                        break;
                }
            }

            _computer = computer;

            // Two update passes: some sensors only populate a value on the second read.
            UpdateHardware();
            LocateSensors();
            UpdateHardware();
            RefreshAvailability();

            return CpuTempAvailable || GpuTempAvailable;
        }
        catch
        {
            Close();
            return false;
        }
    }

    /// <summary>Refreshes the selected sensors; returns (cpuTempC, gpuTempC), each null if unreadable.</summary>
    public (double? CpuTempC, double? GpuTempC) Read()
    {
        if (_computer is null)
            return (null, null);

        try
        {
            UpdateHardware();
            double? cpu = _cpuTempSensor?.Value is float c ? c : null;
            double? gpu = _gpuTempSensor?.Value is float g ? g : null;
            return (cpu, gpu);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Releases the LHM Computer handle (and its ring-0 driver, if loaded).</summary>
    public void Close()
    {
        try { _computer?.Close(); }
        catch { /* ignore */ }
        _computer = null;
        _cpuHardware.Clear();
        _gpuHardware.Clear();
        _cpuTempSensor = null;
        _gpuTempSensor = null;
        CpuTempAvailable = false;
        GpuTempAvailable = false;
    }

    private void UpdateHardware()
    {
        foreach (var hw in _cpuHardware)
        {
            try { hw.Update(); } catch { }
        }
        foreach (var hw in _gpuHardware)
        {
            try { hw.Update(); } catch { }
        }
    }

    private void LocateSensors()
    {
        _cpuTempSensor = PickTemperature(_cpuHardware, CpuPriority);
        _gpuTempSensor = PickTemperature(_gpuHardware, GpuPriority);
    }

    private void RefreshAvailability()
    {
        CpuTempAvailable = _cpuTempSensor?.Value is not null;
        GpuTempAvailable = _gpuTempSensor?.Value is not null;
    }

    // Lower score = more preferred.
    private static int CpuPriority(string name) =>
        name.Equals("Core Average", StringComparison.OrdinalIgnoreCase) ? 0 :
        name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ? 1 :
        name.Contains("Package", StringComparison.OrdinalIgnoreCase) ? 2 :
        name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase) ? 3 :
        name.Contains("Core Max", StringComparison.OrdinalIgnoreCase) ? 4 : 50;

    private static int GpuPriority(string name) =>
        name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ? 0 :
        name.Equals("GPU Temperature", StringComparison.OrdinalIgnoreCase) ? 1 :
        name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ? 2 :
        name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ? 3 : 50;

    private static ISensor? PickTemperature(List<IHardware> hardware, Func<string, int> priority)
    {
        ISensor? best = null;
        int bestScore = int.MaxValue;
        foreach (var hw in hardware)
        {
            foreach (var s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                int score = priority(s.Name);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
        }
        return best;
    }
}
