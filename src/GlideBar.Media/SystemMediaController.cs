using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using GsmtcManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace GlideBar.Media;

/// <summary>
/// Real <see cref="IMediaController"/> backed by the Windows System Media Transport Controls (SMTC).
///
/// <see cref="Start"/> requests the session manager, hooks its change events and each session's
/// metadata/playback/timeline events, and starts a 1 s timer that refreshes just the playback
/// position while open. <see cref="Stop"/> unhooks everything and disposes the timer so idle cost is
/// ~0 (the bar calls Stop whenever the Control Center closes). Full metadata + artwork is rebuilt only
/// on real change events; the timer never re-decodes thumbnails.
///
/// All SMTC callbacks arrive on background threads; this class is internally synchronized and raises
/// <see cref="SessionsChanged"/> on whatever thread the change came in on (the view model marshals).
/// </summary>
public sealed class SystemMediaController : IMediaController
{
    private readonly object _gate = new();

    private GsmtcManager? _manager;
    private TypedEventHandler<GsmtcManager, SessionsChangedEventArgs>? _onSessionsChanged;

    // The live sessions we are subscribed to, with their per-session handler registrations.
    private readonly List<Hooked> _hooked = new();
    private Timer? _timer;
    private volatile bool _running;
    private int _rebuilding; // single-flight guard for the async rebuild

    private volatile IReadOnlyList<MediaSessionInfo> _sessions = Array.Empty<MediaSessionInfo>();

    public IReadOnlyList<MediaSessionInfo> Sessions => _sessions;
    public bool IsRunning => _running;
    public event EventHandler? SessionsChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
        }
        _ = InitAsync();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;

            _timer?.Dispose();
            _timer = null;

            UnhookAll_NoLock();

            if (_manager is not null && _onSessionsChanged is not null)
                _manager.SessionsChanged -= _onSessionsChanged;
            _manager = null;
            _onSessionsChanged = null;

            _sessions = Array.Empty<MediaSessionInfo>();
        }
        Raise();
    }

    public async Task PlayPauseAsync(string sessionId)
    {
        var s = Find(sessionId);
        if (s is null) return;
        try { await s.TryTogglePlayPauseAsync(); } catch { /* app rejected the command */ }
    }

    public async Task NextAsync(string sessionId)
    {
        var s = Find(sessionId);
        if (s is null) return;
        try { await s.TrySkipNextAsync(); } catch { }
    }

    public async Task PreviousAsync(string sessionId)
    {
        var s = Find(sessionId);
        if (s is null) return;
        try { await s.TrySkipPreviousAsync(); } catch { }
    }

    // -------------------------------------------------------------------------------------------
    //  Setup / teardown
    // -------------------------------------------------------------------------------------------
    private async Task InitAsync()
    {
        GsmtcManager mgr;
        try { mgr = await GsmtcManager.RequestAsync(); }
        catch { return; } // SMTC unavailable — leave sessions empty.

        lock (_gate)
        {
            if (!_running) return; // stopped while awaiting
            _manager = mgr;
            _onSessionsChanged = (_, _) => QueueRebuild();
            _manager.SessionsChanged += _onSessionsChanged;
            _timer = new Timer(_ => RefreshTimelines(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        await RebuildAsync();
    }

    private void QueueRebuild() => _ = RebuildAsync();

    private async Task RebuildAsync()
    {
        if (Interlocked.CompareExchange(ref _rebuilding, 1, 0) != 0) return; // coalesce bursts
        try
        {
            GsmtcManager? mgr;
            lock (_gate) { mgr = _manager; }
            if (mgr is null || !_running) return;

            IReadOnlyList<GsmtcSession> live;
            try { live = mgr.GetSessions(); } catch { return; }
            GsmtcSession? current;
            try { current = mgr.GetCurrentSession(); } catch { current = null; }

            // Current session first, then the rest (stable, de-duplicated by AUMID).
            var ordered = new List<GsmtcSession>();
            if (current is not null) ordered.Add(current);
            foreach (var s in live)
                if (!ordered.Any(o => ReferenceEquals(o, s))) ordered.Add(s);

            // Re-hook: drop old registrations, subscribe to the current set.
            lock (_gate)
            {
                if (!_running) return;
                UnhookAll_NoLock();
                foreach (var s in ordered) _hooked.Add(Hook_NoLock(s));
            }

            var infos = new List<MediaSessionInfo>(ordered.Count);
            foreach (var s in ordered)
            {
                var info = await BuildInfoAsync(s);
                if (info is not null) infos.Add(info);
            }

            _sessions = infos;
            Raise();
        }
        finally
        {
            Interlocked.Exchange(ref _rebuilding, 0);
        }
    }

    private Hooked Hook_NoLock(GsmtcSession s)
    {
        TypedEventHandler<GsmtcSession, MediaPropertiesChangedEventArgs> onMedia = (_, _) => QueueRebuild();
        TypedEventHandler<GsmtcSession, PlaybackInfoChangedEventArgs> onPlayback = (_, _) => QueueRebuild();
        TypedEventHandler<GsmtcSession, TimelinePropertiesChangedEventArgs> onTimeline = (_, _) => RefreshTimelines();
        s.MediaPropertiesChanged += onMedia;
        s.PlaybackInfoChanged += onPlayback;
        s.TimelinePropertiesChanged += onTimeline;
        return new Hooked(s, onMedia, onPlayback, onTimeline);
    }

    private void UnhookAll_NoLock()
    {
        foreach (var h in _hooked)
        {
            try
            {
                h.Session.MediaPropertiesChanged -= h.OnMedia;
                h.Session.PlaybackInfoChanged -= h.OnPlayback;
                h.Session.TimelinePropertiesChanged -= h.OnTimeline;
            }
            catch { /* session already gone */ }
        }
        _hooked.Clear();
    }

    // -------------------------------------------------------------------------------------------
    //  Reads
    // -------------------------------------------------------------------------------------------
    private static async Task<MediaSessionInfo?> BuildInfoAsync(GsmtcSession s)
    {
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var playback = s.GetPlaybackInfo();
            var timeline = s.GetTimelineProperties();

            var state = MapState(playback?.PlaybackStatus);
            string title = props?.Title ?? "";
            // Skip empty/closed ghost sessions (e.g. a browser tab that registered but has no media).
            if (string.IsNullOrWhiteSpace(title) &&
                state is MediaPlaybackState.Closed or MediaPlaybackState.Stopped or MediaPlaybackState.Unknown)
                return null;

            byte[]? artwork = await ReadThumbnailAsync(props?.Thumbnail);
            var controls = playback?.Controls;
            string aumid = SafeAumid(s);

            return new MediaSessionInfo
            {
                SessionId = aumid,
                AppId = aumid,
                AppDisplayName = FriendlyAppName(aumid),
                Title = title,
                Artist = props?.Artist ?? "",
                Album = props?.AlbumTitle ?? "",
                State = state,
                Position = timeline?.Position ?? TimeSpan.Zero,
                Duration = timeline?.EndTime ?? TimeSpan.Zero,
                CanPlayPause = controls?.IsPlayEnabled == true || controls?.IsPauseEnabled == true,
                CanNext = controls?.IsNextEnabled == true,
                CanPrevious = controls?.IsPreviousEnabled == true,
                Artwork = artwork,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Cheap tick: refresh only position/playback on the existing snapshots (no artwork).</summary>
    private void RefreshTimelines()
    {
        List<Hooked> hooked;
        lock (_gate)
        {
            if (!_running) return;
            hooked = new List<Hooked>(_hooked);
        }

        var byId = new Dictionary<string, (TimeSpan Pos, TimeSpan Dur, MediaPlaybackState State)>();
        foreach (var h in hooked)
        {
            try
            {
                var tl = h.Session.GetTimelineProperties();
                var pb = h.Session.GetPlaybackInfo();
                byId[SafeAumid(h.Session)] = (tl?.Position ?? TimeSpan.Zero, tl?.EndTime ?? TimeSpan.Zero, MapState(pb?.PlaybackStatus));
            }
            catch { /* session vanished; next rebuild will drop it */ }
        }
        if (byId.Count == 0) return;

        var updated = _sessions.Select(info =>
            byId.TryGetValue(info.SessionId, out var t)
                ? info with { Position = t.Pos, Duration = t.Dur == TimeSpan.Zero ? info.Duration : t.Dur, State = t.State }
                : info).ToList();

        _sessions = updated;
        Raise();
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream is null || stream.Size == 0) return null;
            uint size = (uint)stream.Size;
            var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch { return null; }
    }

    private GsmtcSession? Find(string sessionId)
    {
        lock (_gate)
            return _hooked.FirstOrDefault(h => SafeAumid(h.Session) == sessionId)?.Session;
    }

    private void Raise() => SessionsChanged?.Invoke(this, EventArgs.Empty);

    private static string SafeAumid(GsmtcSession s)
    {
        try { return s.SourceAppUserModelId ?? ""; } catch { return ""; }
    }

    private static MediaPlaybackState MapState(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackState.Changing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackState.Opened,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackState.Closed,
        _ => MediaPlaybackState.Unknown,
    };

    /// <summary>Best-effort friendly name from an AUMID. Full shell resolution is a later enhancement.</summary>
    private static string FriendlyAppName(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "Media";
        string s = aumid;
        int bang = s.IndexOf('!');                 // packaged: "<pfn>!<appid>" -> appid
        if (bang >= 0 && bang < s.Length - 1) s = s[(bang + 1)..];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4]; // "Spotify.exe" -> "Spotify"
        int slash = s.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0 && slash < s.Length - 1) s = s[(slash + 1)..];
        return string.IsNullOrWhiteSpace(s) ? "Media" : s;
    }

    public void Dispose() => Stop();

    private sealed record Hooked(
        GsmtcSession Session,
        TypedEventHandler<GsmtcSession, MediaPropertiesChangedEventArgs> OnMedia,
        TypedEventHandler<GsmtcSession, PlaybackInfoChangedEventArgs> OnPlayback,
        TypedEventHandler<GsmtcSession, TimelinePropertiesChangedEventArgs> OnTimeline);
}
