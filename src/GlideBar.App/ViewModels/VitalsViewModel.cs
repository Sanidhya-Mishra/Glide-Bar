using CommunityToolkit.Mvvm.ComponentModel;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.App.ViewModels;

/// <summary>
/// Live-vitals zone: four ring gauges (CPU / RAM / GPU / Temp). Values + preformatted display
/// strings are updated from each <see cref="SystemSnapshot"/>. Temperature visibility follows the
/// provider capabilities so the gauge hides gracefully when temps aren't available.
/// </summary>
public sealed partial class VitalsViewModel : ObservableObject
{
    [ObservableProperty] private double _cpuLoad;
    [ObservableProperty] private double _gpuLoad;
    [ObservableProperty] private double _ramUsedPercent;
    [ObservableProperty] private double _tempValue;        // whichever temp we show (CPU preferred, else GPU)

    [ObservableProperty] private string _cpuDisplay = "--";
    [ObservableProperty] private string _gpuDisplay = "--";
    [ObservableProperty] private string _ramDisplay = "--";
    [ObservableProperty] private string _tempDisplay = "--";
    [ObservableProperty] private string _ramDetail = "";   // e.g. "10.9 / 16.0 GB"

    [ObservableProperty] private bool _tempAvailable = true;

    public void ApplyCapabilities(MetricsCapabilities caps)
        => TempAvailable = caps.CpuTemp || caps.GpuTemp;

    /// <summary>Marshal to the UI thread before calling (the snapshot event is background-thread).</summary>
    public void Update(SystemSnapshot s)
    {
        CpuLoad = s.CpuLoadPercent;
        GpuLoad = s.GpuLoadPercent;
        RamUsedPercent = s.RamUsedPercent;

        CpuDisplay = $"{Math.Round(s.CpuLoadPercent)}%";
        GpuDisplay = $"{Math.Round(s.GpuLoadPercent)}%";
        RamDisplay = $"{Math.Round(s.RamUsedPercent)}%";
        RamDetail = $"{Bytes(s.RamUsedBytes)} / {Bytes(s.RamTotalBytes)}";

        double? temp = s.CpuTempC ?? s.GpuTempC;
        if (temp is { } tc)
        {
            TempValue = tc;
            TempDisplay = $"{Math.Round(tc)}°";
        }
        else
        {
            TempDisplay = "--";
        }
    }

    private static string Bytes(long b)
    {
        const double gb = 1024d * 1024 * 1024;
        return b <= 0 ? "0 GB" : $"{b / gb:0.0} GB";
    }
}
