using System.IO;
using System.Windows;
using GlideBar.App.Services;
using GlideBar.App.ViewModels;
using GlideBar.App.Views;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GlideBar.App;

/// <summary>
/// Application entry point. Builds the DI container (see <see cref="CompositionRoot"/>), loads
/// settings, applies the theme, shows the overlay (collapsed), and registers the tray icon.
/// Lives in the tray — closing the window never quits; only the tray "Quit" or hotkey-driven
/// shutdown does.
/// </summary>
public partial class App : Application
{
    private IServiceProvider _services = null!;
    private TrayIcon? _tray;
    private BarWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray-resident

        _services = CompositionRoot.Build();

        // Settings.
        var settings = _services.GetRequiredService<ISettingsService>();
        settings.LoadAsync().GetAwaiter().GetResult();

        // First run (no pins yet): seed a few handy shortcuts so the bar isn't empty.
        if (settings.Settings.Pins.Count == 0)
        {
            void Seed(string name, string path)
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    settings.AddPin(new PinnedItem { Name = name, Path = path, Kind = PinnedKind.Folder });
            }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Seed("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Seed("Downloads", Path.Combine(home, "Downloads"));
            Seed("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        }

        // Theme before any window shows, so first paint is correct.
        var theme = _services.GetRequiredService<ThemeManager>();
        theme.SetPreference(settings.Settings.Theme);

        // Tray.
        _tray = new TrayIcon(_services.GetRequiredService<IAutostartService>());
        var vm = _services.GetRequiredService<BarViewModel>();
        _tray.ShowHideRequested += () => _window?.ToggleSummon();
        _tray.SettingsRequested += () => { _window?.Summon(); vm.IsSettingsOpen = true; };
        _tray.PauseToggled += paused => { vm.SetMonitoringPaused(paused); _tray?.SetPaused(paused); };
        _tray.QuitRequested += Shutdown;
        _tray.Create();

        // Overlay window. Show it, then Summon so the first run is fully visible (expanded + pinned)
        // instead of collapsed to an easy-to-miss sliver. Ctrl+Space (or the tray) collapses it;
        // after that, hover the anchored edge to reveal it again.
        _window = _services.GetRequiredService<BarWindow>();
        _window.Show();
        _window.Summon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); } catch { }
        try { _services.GetService<BarViewModel>()?.Dispose(); } catch { }
        (_services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
