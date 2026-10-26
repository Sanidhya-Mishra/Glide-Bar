using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.App.ViewModels;

/// <summary>
/// One media card in the Now Playing section. Updated in place (matched by <see cref="SessionId"/>) so
/// the album-art bitmap is only re-decoded when the artwork bytes actually change — a 1 s position
/// refresh never rebuilds the image.
/// </summary>
public sealed partial class MediaSessionViewModel : ObservableObject
{
    private readonly IMediaController _controller;
    private byte[]? _artworkBytes;

    public string SessionId { get; }

    [ObservableProperty] private string _appName = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";   // "Artist · Album"
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private double _progress;          // 0..1 for the ProgressBar
    [ObservableProperty] private bool _canNext;
    [ObservableProperty] private bool _canPrevious;
    [ObservableProperty] private bool _canPlayPause;
    [ObservableProperty] private ImageSource? _artwork;

    public bool HasArtwork => Artwork is not null;
    partial void OnArtworkChanged(ImageSource? value) => OnPropertyChanged(nameof(HasArtwork));

    public MediaSessionViewModel(MediaSessionInfo info, IMediaController controller)
    {
        _controller = controller;
        SessionId = info.SessionId;
        Update(info);
    }

    /// <summary>Apply a fresh snapshot. Call on the UI thread.</summary>
    public void Update(MediaSessionInfo info)
    {
        AppName = info.AppDisplayName;
        Title = string.IsNullOrWhiteSpace(info.Title) ? "Unknown title" : info.Title;
        Subtitle = JoinMeta(info.Artist, info.Album);
        IsPlaying = info.IsPlaying;
        CanPlayPause = info.CanPlayPause;
        CanNext = info.CanNext;
        CanPrevious = info.CanPrevious;

        PositionText = Format(info.Position);
        DurationText = info.Duration > TimeSpan.Zero ? Format(info.Duration) : "--:--";
        Progress = info.Duration > TimeSpan.Zero
            ? Math.Clamp(info.Position.TotalSeconds / info.Duration.TotalSeconds, 0, 1)
            : 0;

        // Re-decode the bitmap only when the artwork bytes changed.
        if (!BytesEqual(_artworkBytes, info.Artwork))
        {
            _artworkBytes = info.Artwork;
            Artwork = Decode(info.Artwork);
        }
    }

    [RelayCommand] private Task PlayPause() => _controller.PlayPauseAsync(SessionId);
    [RelayCommand] private Task Next() => _controller.NextAsync(SessionId);
    [RelayCommand] private Task Previous() => _controller.PreviousAsync(SessionId);

    private static string JoinMeta(string artist, string album)
    {
        artist = artist?.Trim() ?? "";
        album = album?.Trim() ?? "";
        if (artist.Length > 0 && album.Length > 0) return $"{artist} · {album}";
        return artist.Length > 0 ? artist : album;
    }

    private static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static bool BytesEqual(byte[]? a, byte[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Length != b.Length) return false;
        return a.AsSpan().SequenceEqual(b);
    }

    private static ImageSource? Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.DecodePixelWidth = 128; // card art is ~64px; decode small to save memory
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}

/// <summary>
/// The Now Playing section: mirrors <see cref="IMediaController.Sessions"/> into an observable
/// collection, reconciling updates in place (by session id). Owns the controller lifecycle — it is
/// <see cref="Start"/>ed only while the Control Center is open and <see cref="Stop"/>ped on close, so
/// SMTC listening costs nothing at idle. Controller events arrive on background threads and are
/// marshalled to the UI thread here.
/// </summary>
public sealed partial class MediaViewModel : ObservableObject, IDisposable
{
    private readonly IMediaController _controller;

    public ObservableCollection<MediaSessionViewModel> Sessions { get; } = new();

    [ObservableProperty] private bool _hasSessions;

    public MediaViewModel(IMediaController controller)
    {
        _controller = controller;
        _controller.SessionsChanged += OnSessionsChanged;
    }

    /// <summary>Begin listening (Control Center opened).</summary>
    public void Start()
    {
        if (!_controller.IsRunning) _controller.Start();
        Reconcile(_controller.Sessions); // reflect whatever is already playing immediately
    }

    /// <summary>Stop listening (Control Center closed) and clear the cards.</summary>
    public void Stop()
    {
        if (_controller.IsRunning) _controller.Stop();
        Sessions.Clear();
        HasSessions = false;
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        var snapshot = _controller.Sessions;
        var d = Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) Reconcile(snapshot);
        else d.BeginInvoke(() => Reconcile(snapshot));
    }

    private void Reconcile(IReadOnlyList<MediaSessionInfo> infos)
    {
        // Remove cards whose session disappeared.
        for (int i = Sessions.Count - 1; i >= 0; i--)
            if (!infos.Any(x => x.SessionId == Sessions[i].SessionId))
                Sessions.RemoveAt(i);

        // Add / update / reorder to match the incoming order.
        for (int i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            var existing = Sessions.FirstOrDefault(s => s.SessionId == info.SessionId);
            if (existing is null)
            {
                Sessions.Insert(Math.Min(i, Sessions.Count), new MediaSessionViewModel(info, _controller));
            }
            else
            {
                existing.Update(info);
                int cur = Sessions.IndexOf(existing);
                if (cur != i && i < Sessions.Count) Sessions.Move(cur, i);
            }
        }

        HasSessions = Sessions.Count > 0;
    }

    public void Dispose()
    {
        _controller.SessionsChanged -= OnSessionsChanged;
        _controller.Stop();
    }
}
