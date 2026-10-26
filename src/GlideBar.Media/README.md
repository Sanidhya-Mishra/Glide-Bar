# GlideBar.Media

Real `IMediaController` for GlideBar's **Now Playing** panel. Wraps the Windows **System Media
Transport Controls** (SMTC) so any media app — Spotify, a browser, a local player — shows up with no
per-app integration.

## What it reads (per session)

- **App** — `SourceAppUserModelId` (AUMID), cleaned to a friendly name (full shell resolution is TODO).
- **Metadata** — `Title`, `Artist`, `AlbumTitle`, and the artwork `Thumbnail` (decoded to bytes).
- **Playback** — status (Playing/Paused/…) and which controls the app supports (play/pause/next/prev).
- **Timeline** — current position + end time (for the progress indicator).

## Transport

`PlayPauseAsync` / `NextAsync` / `PreviousAsync` relay to the session via
`TryTogglePlayPauseAsync` / `TrySkipNextAsync` / `TrySkipPreviousAsync`. Commands the app rejects are
swallowed.

## Lifecycle / performance

- `Start()` requests `GlobalSystemMediaTransportControlsSessionManager`, hooks its `SessionsChanged`
  plus each session's metadata/playback/timeline events, and starts a 1 s timer that refreshes **only**
  position/playback (never re-decodes artwork).
- Full metadata + artwork is rebuilt **only on real change events**; bursts are coalesced (single-flight).
- **`Stop()` unhooks every event and disposes the timer** — the UI calls it whenever the Control Center
  closes, so idle cost is ~0. SMTC callbacks arrive on background threads; the view model marshals.

## DI

```csharp
services.AddGlideBarMedia(); // registers IMediaController (singleton)
```

## Notes

- Targets `net10.0-windows10.0.19041.0` for the WinRT projection; min OS is Windows 10 1809+.
- If SMTC is unavailable, the controller degrades to an empty session list (no throw).
- **Notifications are not handled here.** Reading the OS notification list needs
  `UserNotificationListener`, which requires MSIX packaging + user consent — tracked as future work.
