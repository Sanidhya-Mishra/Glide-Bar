using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.Storage;

/// <summary>
/// Owns the live <see cref="AppSettings"/> and the pinned-item CRUD, persisting through
/// <see cref="IStorage"/>. Saves are <b>debounced</b> (~500 ms, rapid calls coalesce into a
/// single write) and all mutation is guarded by a lock, so it is safe to call from any thread.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    /// <summary>Current config schema. Bump this when the persisted shape changes, and add a case to <see cref="Migrate"/>.</summary>
    private const int CurrentSchemaVersion = 1;

    /// <summary>Logical storage key → <c>%AppData%\GlideBar\config.json</c> (matches <see cref="Core.AppPaths.ConfigFile"/>).</summary>
    private const string ConfigKey = "config";

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly IStorage _storage;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private AppSettings _settings = new();
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    public SettingsService(IStorage storage) => _storage = storage;

    public AppSettings Settings
    {
        get { lock (_gate) return _settings; }
    }

    public event EventHandler? SettingsChanged;
    public event EventHandler? PinsChanged;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var loaded = await _storage.LoadAsync<AppSettings>(ConfigKey, ct).ConfigureAwait(false);

        lock (_gate)
        {
            // Missing/corrupt => defaults (AppSettings already seeds an empty Pins list).
            _settings = loaded is null ? new AppSettings() : Migrate(loaded);
            Normalize(_settings);
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        PinsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Schedules a debounced save and returns a task that completes when either this save runs
    /// or a later call supersedes it. Multiple rapid calls collapse to one actual write.
    /// </summary>
    public Task SaveAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_disposed)
                return Task.CompletedTask;

            // Cancel the in-flight debounce window; the newest call wins.
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _debounceCts = cts;
            return DebouncedSaveAsync(cts.Token);
        }
    }

    private async Task DebouncedSaveAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer SaveAsync (or caller cancelled) — coalesced away
        }

        AppSettings snapshot;
        lock (_gate)
        {
            snapshot = Clone(_settings);
        }

        // Serialize writes so two flushes never interleave on disk.
        await _writeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await _storage.SaveAsync(ConfigKey, snapshot, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void AddPin(PinnedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            var nextOrder = _settings.Pins.Count == 0 ? 0 : _settings.Pins.Max(p => p.Order) + 1;
            _settings.Pins.Add(item with { Order = nextOrder });
            Reorder(_settings.Pins);
        }
        OnPinsChanged();
    }

    public void RemovePin(string id)
    {
        bool changed;
        lock (_gate)
        {
            changed = _settings.Pins.RemoveAll(p => p.Id == id) > 0;
            if (changed) Reorder(_settings.Pins);
        }
        if (changed) OnPinsChanged();
    }

    public void MovePin(string id, int newIndex)
    {
        bool changed = false;
        lock (_gate)
        {
            var idx = _settings.Pins.FindIndex(p => p.Id == id);
            if (idx >= 0)
            {
                var item = _settings.Pins[idx];
                _settings.Pins.RemoveAt(idx);
                var target = Math.Clamp(newIndex, 0, _settings.Pins.Count);
                _settings.Pins.Insert(target, item);
                Reorder(_settings.Pins);
                changed = true;
            }
        }
        if (changed) OnPinsChanged();
    }

    private void OnPinsChanged()
    {
        PinsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
    }

    /// <summary>Applies version-to-version upgrades. v1 is current, so it is a no-op stub today.</summary>
    private static AppSettings Migrate(AppSettings s)
    {
        switch (s.SchemaVersion)
        {
            case CurrentSchemaVersion:
                // Current shape — nothing to do.
                break;

            // case 2: ...future migrations chain here (fall through upward)...

            default:
                // Unknown (future/older/garbage) version: stamp to current. Add real handling
                // above as the schema evolves.
                s.SchemaVersion = CurrentSchemaVersion;
                break;
        }
        return s;
    }

    /// <summary>Guards against a partially-populated deserialization and keeps Order consistent.</summary>
    private static void Normalize(AppSettings s)
    {
        s.Pins ??= new List<PinnedItem>();
        // Honour any persisted Order, then compact to a 0..n-1 sequence.
        var ordered = s.Pins.OrderBy(p => p.Order).ToList();
        s.Pins.Clear();
        s.Pins.AddRange(ordered);
        Reorder(s.Pins);
    }

    /// <summary>Rewrites <see cref="PinnedItem.Order"/> to match the current list position.</summary>
    private static void Reorder(List<PinnedItem> pins)
    {
        for (var i = 0; i < pins.Count; i++)
        {
            if (pins[i].Order != i)
                pins[i] = pins[i] with { Order = i };
        }
    }

    /// <summary>Deep-ish copy taken under lock so serialization never races a mutation.</summary>
    private static AppSettings Clone(AppSettings s) => new()
    {
        SchemaVersion = s.SchemaVersion,
        Theme = s.Theme,
        PollIntervalMs = s.PollIntervalMs,
        TemperaturesEnabled = s.TemperaturesEnabled,
        Autostart = s.Autostart,
        ReduceEffects = s.ReduceEffects,
        Hotkey = s.Hotkey,
        Placement = s.Placement,                 // record (immutable) — safe to share
        Pins = new List<PinnedItem>(s.Pins),     // records inside — safe to share elements
    };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
        _writeLock.Dispose();
    }
}
