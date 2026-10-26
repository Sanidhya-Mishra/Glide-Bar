# Architecture notes

Deeper technical detail behind [`../PLAN.md`](../PLAN.md). Recommended stack: **WPF / .NET 8**.

## Projects & dependencies

| Project | Type | Depends on | Responsibility |
|---------|------|-----------|----------------|
| `GlideBar.Core` | classlib | — | Models, interfaces, DI extensions, events |
| `GlideBar.HardwareMonitor` | classlib | Core | Metrics (perf counters + LibreHardwareMonitor) |
| `GlideBar.Search` | classlib | Core | File search providers + launcher |
| `GlideBar.Storage` | classlib | Core | JSON settings + pinned-item persistence |
| `GlideBar.Media` | classlib | Core | Media sessions via SMTC (`IMediaController`) |
| `GlideBar.App` | WPF exe | Core + all services (DI) | Overlay window, interaction, tray, views, theme |

`GlideBar.Media` and `GlideBar.App` target `net10.0-windows10.0.19041.0` (the WinRT projection needed
for the System Media Transport Controls); the other projects stay on `net10.0` / `net10.0-windows`.

## Metrics: layered, cheap-first

| Metric | Primary source (no admin) | Notes |
|--------|---------------------------|-------|
| CPU %  | `GetSystemTimes` delta     | Cheaper & steadier than the `% Processor Time` counter |
| RAM    | `GlobalMemoryStatusEx`     | Total/used/available |
| GPU %  | `GPU Engine` perf counters | Sum across engines; present on Win10 1709+ |
| GPU mem| `GPU Adapter Memory`       | Dedicated/shared |
| CPU temp | LibreHardwareMonitor     | **Needs elevation** (kernel driver) |
| GPU temp | LibreHardwareMonitor     | Often works without admin (NVAPI/ADL) |

Polling is driven by one shared timer that only runs while the bar is visible. When hidden, the provider
stops the timer entirely (no background sampling), which is the core of the low-idle-cost guarantee.

## Media sessions (Now Playing)

`IMediaController` (impl `GlideBar.Media.SystemMediaController`) wraps the **System Media Transport
Controls**: `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` enumerates every app
holding a media session (Spotify, browsers, local players). Per session we read title/artist/album,
the artwork thumbnail (`IRandomAccessStreamReference` → bytes), playback status + available controls,
and timeline position/duration; transport is relayed via `TryTogglePlayPause/SkipNext/SkipPrevious`.

Like metrics, it is **lifecycle-bound**: `Start()` only while the Control Center is open (hooks SMTC
change events + a 1 s timeline-refresh timer), `Stop()` on close unhooks everything → ~0 idle cost.
Full metadata/artwork is rebuilt only on change events; the timer refreshes just position/playback.
Notifications are **not** sourced here — `UserNotificationListener` needs MSIX packaging (future work).

## File search

1. **Everything SDK (voidtools)** — if `Everything.exe`/IPC is available, query it: instant, trivial cost.
2. **Windows Search** — default. OLE DB query against `provider=Search.CollatorDSO` (`SystemIndex`), e.g.
   `SELECT System.ItemPathDisplay FROM SystemIndex WHERE SCOPE='file:' AND System.FileName LIKE '%q%'`.
   Reuses the OS indexer; we never maintain our own always-on index.
3. **Scoped enumeration** — only for pinned folders / recent, on demand.

Queries are debounced (~150 ms), cancellable, capped (e.g. top 30), ranked (prefix > substring, recency, pins).

## Launcher actions
- **Open** — `ShellExecuteEx` / `Process.Start(UseShellExecute=true)`.
- **Reveal** — `explorer.exe /select,"path"`.
- **Open folder** — open containing directory.
- **Pinned app** — resolve `.lnk`, or `shell:AppsFolder\<AUMID>` for UWP/Store apps.

## Window & interaction
- Overlay window: `WS_EX_TOOLWINDOW` (no alt-tab/taskbar), `Topmost`, `AllowsTransparency`, `WindowStyle=None`.
- **Trigger:** a thin region at the active edge; `WM_MOUSEMOVE`/mouse-enter reveals after a short dwell;
  mouse-leave hides after a grace period. Prefer an edge trigger window over a global mouse hook (far cheaper).
- **Drag & snap:** drag anywhere on the chrome; snap to nearest edge/corner; store `BarPosition` (monitor id + edge + offset).
- **Multi-monitor:** per-monitor DPI aware; position validated against current monitor layout on startup.
- **Global hotkey:** `RegisterHotKey` (default `Ctrl`+`Space`, configurable) to summon/dismiss.
- **Tray:** H.NotifyIcon — show/hide, settings, pause, quit.
- **Autostart:** opt-in; `HKCU\...\Run` value or a Startup-folder shortcut.

## Persistence
`%AppData%\GlideBar\config.json` — `{ schemaVersion, position, theme, pollIntervalMs, enabledSensors[], autostart, pins[] }`.
Atomic write (write temp → `File.Replace`); saves debounced ~500 ms.

## Theming
WPF `ResourceDictionary` token set (colors, radii, blur, type scale) with light/dark variants, swapped at
runtime. DWM backdrop (acrylic/mica) via `DwmSetWindowAttribute` where available; CSS-like fallback solid
surface when "reduce effects" is on. The chosen mockup defines the concrete token values + control templates.
