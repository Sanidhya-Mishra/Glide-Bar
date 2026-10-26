using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using GlideBar.Core.Abstractions;
using H.NotifyIcon;

namespace GlideBar.App.Services;

/// <summary>
/// System-tray presence (H.NotifyIcon) with the context menu required by the shell:
/// Show/Hide · Pause monitoring · Settings · Start with Windows · Quit. All creation is guarded so
/// a tray failure never prevents the app from launching.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly IAutostartService _autostart;
    private TaskbarIcon? _icon;
    private MenuItem? _pauseItem;
    private MenuItem? _autostartItem;

    public event Action? ShowHideRequested;
    public event Action<bool>? PauseToggled;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public TrayIcon(IAutostartService autostart) => _autostart = autostart;

    public void Create()
    {
        try
        {
            _icon = new TaskbarIcon
            {
                ToolTipText = "GlideBar",
                Icon = BuildIcon(),
                LeftClickCommand = new RelayCommand(() => ShowHideRequested?.Invoke()),
                ContextMenu = BuildMenu(),
            };
            _icon.ForceCreate();
        }
        catch
        {
            // Tray unavailable (rare) — app continues to run; hotkey still summons the bar.
            _icon = null;
        }
    }

    public void SetPaused(bool paused)
    {
        if (_pauseItem is not null) _pauseItem.IsChecked = paused;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var show = new MenuItem { Header = "Show / Hide" };
        show.Click += (_, _) => ShowHideRequested?.Invoke();

        _pauseItem = new MenuItem { Header = "Pause monitoring", IsCheckable = true };
        _pauseItem.Click += (_, _) => PauseToggled?.Invoke(_pauseItem.IsChecked);

        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => SettingsRequested?.Invoke();

        _autostartItem = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = _autostart.IsEnabled };
        _autostartItem.Click += (_, _) => _autostart.SetEnabled(_autostartItem.IsChecked);

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => QuitRequested?.Invoke();

        menu.Items.Add(show);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        return menu;
    }

    /// <summary>Renders a simple indigo "G" tile via H.NotifyIcon's generator (no .ico asset needed).</summary>
    private static System.Drawing.Icon? BuildIcon()
    {
        try
        {
            var gen = new GeneratedIconSource
            {
                Text = "G",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x5B, 0x7C, 0xFA)),
                FontWeight = FontWeights.Bold,
                FontSize = 80,
                CornerRadius = new CornerRadius(26),
                Size = 128,
            };
            return gen.ToIcon();
        }
        catch { return null; }
    }

    public void Dispose()
    {
        try { _icon?.Dispose(); } catch { }
        _icon = null;
    }
}
