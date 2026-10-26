namespace GlideBar.Core.Models;

/// <summary>Which screen edge the bar is anchored to (None = free-floating).</summary>
public enum BarEdge { None, Left, Right, Top, Bottom }

/// <summary>Layout orientation. Vertical is used when docked to Left/Right.</summary>
public enum BarOrientation { Horizontal, Vertical }

public enum PinnedKind { File, Folder, App }

public enum SearchItemKind { File, Folder, App }

/// <summary>One snapshot of live system vitals. Temperatures are null when unavailable.</summary>
public sealed record SystemSnapshot
{
    public double CpuLoadPercent { get; init; }
    public double GpuLoadPercent { get; init; }
    public double RamUsedPercent { get; init; }
    public long RamUsedBytes { get; init; }
    public long RamTotalBytes { get; init; }
    public long GpuMemUsedBytes { get; init; }
    public long GpuMemTotalBytes { get; init; }
    public double? CpuTempC { get; init; }
    public double? GpuTempC { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public static SystemSnapshot Empty { get; } = new();
}

/// <summary>A pinned file, folder or app shown in the bar for one-click access.</summary>
public sealed record PinnedItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public PinnedKind Kind { get; init; }
    public string? Arguments { get; init; }
    public int Order { get; init; }
}

/// <summary>A single file-search hit.</summary>
public sealed record SearchResult
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public SearchItemKind Kind { get; init; }
    public double Score { get; init; }
    public DateTimeOffset? Modified { get; init; }
}

/// <summary>Where the bar lives + how it is oriented. Persisted across sessions.</summary>
public sealed record BarPlacement
{
    public int MonitorIndex { get; init; }
    public BarEdge Edge { get; init; } = BarEdge.Top;
    public bool Floating { get; init; } = true;
    public double X { get; init; }
    public double Y { get; init; }
    public BarOrientation Orientation { get; init; } = BarOrientation.Horizontal;
}

/// <summary>All persisted user settings.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "system"; // "system" | "light" | "dark"
    public int PollIntervalMs { get; set; } = 1000;
    public bool TemperaturesEnabled { get; set; }
    public bool Autostart { get; set; }
    public bool ReduceEffects { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Space";
    public BarPlacement Placement { get; set; } = new();
    public List<PinnedItem> Pins { get; set; } = new();
}
