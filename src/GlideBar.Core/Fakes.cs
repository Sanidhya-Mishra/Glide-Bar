using System.Diagnostics;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.Core.Fakes;

// Dev-only implementations so the UI (Agent D) can build and run before the real
// services (Agents A/B/C) are finished. The App wires these when no real impl is registered.

/// <summary>Emits smoothly oscillating fake vitals on a timer.</summary>
public sealed class FakeMetricsProvider : IMetricsProvider
{
    private readonly object _gate = new();
    private Timer? _timer;
    private int _tick;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);

    public SystemSnapshot Current { get; private set; } = SystemSnapshot.Empty;
    public MetricsCapabilities Capabilities { get; } = new(true, true, true, true, true);
    public bool IsRunning { get; private set; }
    public event EventHandler<SystemSnapshot>? SampleProduced;

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) return;
            IsRunning = true;
            _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, _interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            IsRunning = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void SetInterval(TimeSpan interval)
    {
        _interval = interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : interval;
        lock (_gate) { if (IsRunning) _timer?.Change(TimeSpan.Zero, _interval); }
    }

    public Task<bool> TryEnableTemperaturesAsync(CancellationToken ct = default) => Task.FromResult(true);

    private void Sample()
    {
        var t = Interlocked.Increment(ref _tick);
        double Osc(double mid, double amp, double phase) =>
            Math.Clamp(mid + amp * Math.Sin((t + phase) / 6.0), 0, 100);

        const long totalRam = 16L * 1024 * 1024 * 1024;
        const long totalVram = 8L * 1024 * 1024 * 1024;
        var ramPct = Osc(68, 6, 2);
        var vramPct = Osc(40, 10, 1);

        var snap = new SystemSnapshot
        {
            CpuLoadPercent = Osc(32, 22, 0),
            GpuLoadPercent = Osc(24, 20, 3),
            RamUsedPercent = ramPct,
            RamUsedBytes = (long)(totalRam * ramPct / 100),
            RamTotalBytes = totalRam,
            GpuMemTotalBytes = totalVram,
            GpuMemUsedBytes = (long)(totalVram * vramPct / 100),
            CpuTempC = 45 + 12 * Math.Sin(t / 7.0),
            GpuTempC = 50 + 10 * Math.Sin(t / 8.0),
            TimestampUtc = DateTimeOffset.UtcNow,
        };
        Current = snap;
        SampleProduced?.Invoke(this, snap);
    }

    public void Dispose() => Stop();
}

/// <summary>Emits two advancing media sessions on a timer, so the Control Center works in dev.</summary>
public sealed class FakeMediaController : IMediaController
{
    private readonly object _gate = new();
    private Timer? _timer;
    private int _tick;

    private static readonly (string App, string AppName, string Title, string Artist, string Album, int Dur)[] Seed =
    {
        ("Spotify.exe", "Spotify", "Midnight City", "M83", "Hurry Up, We're Dreaming", 238),
        ("Microsoft.Media.Player", "Media Player", "Feather", "Nujabes", "Modal Soul", 402),
    };

    public IReadOnlyList<MediaSessionInfo> Sessions { get; private set; } = Array.Empty<MediaSessionInfo>();
    public bool IsRunning { get; private set; }
    public event EventHandler? SessionsChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) return;
            IsRunning = true;
            _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            IsRunning = false;
            _timer?.Dispose();
            _timer = null;
            Sessions = Array.Empty<MediaSessionInfo>();
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task PlayPauseAsync(string sessionId) => Task.CompletedTask;
    public Task NextAsync(string sessionId) => Task.CompletedTask;
    public Task PreviousAsync(string sessionId) => Task.CompletedTask;

    private void Sample()
    {
        var t = Interlocked.Increment(ref _tick);
        Sessions = Seed.Select((s, i) =>
        {
            int pos = (t + i * 40) % s.Dur;
            return new MediaSessionInfo
            {
                SessionId = s.App,
                AppId = s.App,
                AppDisplayName = s.AppName,
                Title = s.Title,
                Artist = s.Artist,
                Album = s.Album,
                State = i == 0 ? MediaPlaybackState.Playing : MediaPlaybackState.Paused,
                Position = TimeSpan.FromSeconds(pos),
                Duration = TimeSpan.FromSeconds(s.Dur),
                CanPlayPause = true,
                CanNext = true,
                CanPrevious = true,
            };
        }).ToList();
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();
}

/// <summary>Filters a small sample set so the search box works in dev.</summary>
public sealed class FakeSearchProvider : ISearchProvider
{
    public string Name => "Sample (dev)";
    public bool IsAvailable => true;

    private static readonly SearchResult[] Sample =
    {
        new() { Name = "Q3-report.pdf",       Path = @"C:\Users\You\Documents\Q3-report.pdf",           Kind = SearchItemKind.File },
        new() { Name = "Projects",            Path = @"C:\Users\You\Projects",                          Kind = SearchItemKind.Folder },
        new() { Name = "q3-data.xlsx",        Path = @"C:\Users\You\Desktop\q3-data.xlsx",              Kind = SearchItemKind.File },
        new() { Name = "Visual Studio Code",  Path = @"C:\Users\You\AppData\Local\Programs\VS Code\Code.exe", Kind = SearchItemKind.App },
        new() { Name = "Downloads",           Path = @"C:\Users\You\Downloads",                         Kind = SearchItemKind.Folder },
        new() { Name = "notes.md",            Path = @"C:\Users\You\Documents\notes.md",                Kind = SearchItemKind.File },
    };

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int max, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim();
        IReadOnlyList<SearchResult> results = Sample
            .Where(s => q.Length == 0 || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(s => s with { Score = s.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.5 })
            .OrderByDescending(s => s.Score)
            .Take(max)
            .ToList();
        return Task.FromResult(results);
    }
}

/// <summary>Really opens things via the shell (safe to use in dev).</summary>
public sealed class ShellLauncher : ILauncher
{
    public void Open(SearchResult result) => OpenPath(result.Path);
    public void Open(PinnedItem item) => OpenPath(item.Path);

    public void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* ignore in dev */ }
    }

    public void RevealInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    public void OpenContainingFolder(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch { }
    }
}

/// <summary>In-memory settings with a few seeded pins; no persistence.</summary>
public sealed class InMemorySettingsService : ISettingsService
{
    public AppSettings Settings { get; } = new();
    public event EventHandler? SettingsChanged;
    public event EventHandler? PinsChanged;

    public InMemorySettingsService()
    {
        Settings.Pins.AddRange(new[]
        {
            new PinnedItem { Name = "VS Code",   Path = @"C:\Users\You\AppData\Local\Programs\VS Code\Code.exe", Kind = PinnedKind.App,    Order = 0 },
            new PinnedItem { Name = "Projects",  Path = @"C:\Users\You\Projects",                                Kind = PinnedKind.Folder, Order = 1 },
            new PinnedItem { Name = "Downloads", Path = @"C:\Users\You\Downloads",                               Kind = PinnedKind.Folder, Order = 2 },
        });
    }

    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

    public void AddPin(PinnedItem item)
    {
        Settings.Pins.Add(item with { Order = Settings.Pins.Count });
        PinsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemovePin(string id)
    {
        Settings.Pins.RemoveAll(p => p.Id == id);
        PinsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MovePin(string id, int newIndex)
    {
        var idx = Settings.Pins.FindIndex(p => p.Id == id);
        if (idx < 0) return;
        var item = Settings.Pins[idx];
        Settings.Pins.RemoveAt(idx);
        Settings.Pins.Insert(Math.Clamp(newIndex, 0, Settings.Pins.Count), item);
        PinsChanged?.Invoke(this, EventArgs.Empty);
    }
}
