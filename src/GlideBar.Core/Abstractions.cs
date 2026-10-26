using GlideBar.Core.Models;

namespace GlideBar.Core.Abstractions;

/// <summary>Which metrics the current machine/providers can actually supply.</summary>
public sealed record MetricsCapabilities(
    bool CpuLoad, bool RamLoad, bool GpuLoad, bool CpuTemp, bool GpuTemp);

/// <summary>
/// Produces live system vitals. Implementations MUST stop all sampling when <see cref="Stop"/> is
/// called (the bar calls this whenever it collapses), so idle CPU cost is ~0.
/// </summary>
public interface IMetricsProvider : IDisposable
{
    SystemSnapshot Current { get; }
    MetricsCapabilities Capabilities { get; }
    bool IsRunning { get; }

    /// <summary>Raised on each poll tick, on a background thread.</summary>
    event EventHandler<SystemSnapshot>? SampleProduced;

    void Start();
    void Stop();
    void SetInterval(TimeSpan interval);

    /// <summary>Temperatures may need elevation (CPU). Returns true if enabled.</summary>
    Task<bool> TryEnableTemperaturesAsync(CancellationToken ct = default);
}

/// <summary>
/// Surfaces the OS media sessions (Spotify, browsers, local players…) via the Windows System Media
/// Transport Controls, and relays transport commands back to them. Like <see cref="IMetricsProvider"/>,
/// implementations MUST stop all listening/timers when <see cref="Stop"/> is called (the bar does this
/// whenever the Control Center closes), so idle cost is ~0.
/// </summary>
public interface IMediaController : IDisposable
{
    /// <summary>Current sessions, most-relevant (current) first. Empty when nothing is playing.</summary>
    IReadOnlyList<MediaSessionInfo> Sessions { get; }
    bool IsRunning { get; }

    /// <summary>Raised whenever the set of sessions, their metadata, or playback position changes
    /// (on a background thread — marshal before touching UI).</summary>
    event EventHandler? SessionsChanged;

    void Start();
    void Stop();

    /// <summary>Toggle play/pause on the session with the given <see cref="MediaSessionInfo.SessionId"/>.</summary>
    Task PlayPauseAsync(string sessionId);
    Task NextAsync(string sessionId);
    Task PreviousAsync(string sessionId);
}

/// <summary>Finds files/folders/apps. Multiple providers may exist (Everything, Windows Search…).</summary>
public interface ISearchProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int max, CancellationToken ct = default);
}

/// <summary>Opens / reveals / launches items.</summary>
public interface ILauncher
{
    void Open(SearchResult result);
    void Open(PinnedItem item);
    void OpenPath(string path);
    void RevealInExplorer(string path);
    void OpenContainingFolder(string path);
}

/// <summary>Owns <see cref="AppSettings"/> and pinned-item CRUD; persists via <see cref="IStorage"/>.</summary>
public interface ISettingsService
{
    AppSettings Settings { get; }
    event EventHandler? SettingsChanged;
    event EventHandler? PinsChanged;

    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default); // implementation debounces

    void AddPin(PinnedItem item);
    void RemovePin(string id);
    void MovePin(string id, int newIndex);
}

/// <summary>Low-level typed JSON persistence used by <see cref="ISettingsService"/>.</summary>
public interface IStorage
{
    Task<T?> LoadAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SaveAsync<T>(string key, T value, CancellationToken ct = default) where T : class;
}

/// <summary>Toggles "start with Windows" (HKCU Run key or Startup shortcut).</summary>
public interface IAutostartService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}
