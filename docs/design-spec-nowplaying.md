# Design spec — Now Playing + Notifications (Control Center)

A new **Control Center** flyout for GlideBar: open it from the bar to see every app currently playing
media (Spotify, a browser, a local player…) with full artwork + transport controls, alongside a
notifications column. This is the PC equivalent of a phone's media/notification shade, and it extends
the existing bar rather than replacing any of it.

> Approved from the hand-drawn concept + the interactive mockup (`designs/` / the published artifact).
> It follows the **Adaptive Morph Bar** language already in [`design-spec.md`](design-spec.md).

## Trigger & placement

- A thin **grip line** sits at the bottom-center of the expanded bar (in both the horizontal pill and
  the vertical dock). Clicking it opens the Control Center. This is the sketch's "click the line"
  gesture; the line lights up accent on hover and stays lit while the panel is open. It's a
  `ButtonBase`, so the drag/hover logic on the bar body ignores it.
- The Control Center is a **popup centered on the interior-facing side of the bar** (below when the
  bar is at the top/floating, above at the bottom, right of a left-dock, left of a right-dock — an
  edge-aware custom placement, like the Add panel), styled like the Settings / Add flyouts
  (acrylic surface, 16 px radius, edge-light stroke, soft glow). Opening and closing play a
  **scale + drop + fade** transition (driven from `BarWindow` code-behind on the popup's `Opened`
  event and an out-animation before close), honoring the "reduce effects" setting (instant swap).
- While it is open the bar stays expanded (same rule as search/settings being active); closing it, or
  collapsing the bar, closes the panel.

## Layout (maps to the sketch)

```
┌──────────────────────────── Control Center ────────────────────────────┐
│  APPS & FOLDERS   [tile][tile][tile][tile] [ + ]           (pinned row)  │  ← "All the Apps/Folders Line"
│ ─────────────────────────────────────────────────────────────────────── │
│  NOW PLAYING                    │  NOTIFICATIONS                         │
│  ┌───────────────────────────┐  │  (empty / not-yet-available state)     │
│  │ [art] App · ▸ Title        │  │                                        │
│  │       Artist · Album       │  │                                        │
│  │  0:00 ▬▬▬▬▬▬▬▬▬▬ 3:58       │  │                                        │
│  │        ⏮   ⏯   ⏭           │  │                                        │
│  └───────────────────────────┘  │                                        │
│  ┌───────────────────────────┐  │                                        │
│  │ … one card per media app   │  │                                        │
│  └───────────────────────────┘  │                                        │
└─────────────────────────────────────────────────────────────────────────┘
```

At phone-narrow sizes the two columns stack; here (a fixed-width desktop popup) they sit side by side.

### Media card (per session) — the reusable `NowPlayingCard`

| Element | Source (SMTC) | Sketch label |
|---|---|---|
| Album art (64 px) | `MediaProperties.Thumbnail` | "Music label image" |
| App name + playing indicator | `Session.SourceAppUserModelId` (friendly) + `PlaybackStatus` | "App icon + App name" |
| Title | `MediaProperties.Title` | "Details of music" |
| Artist · Album | `MediaProperties.Artist` / `AlbumTitle` | "Details of music" |
| Progress | `TimelineProperties.Position` / `EndTime` | "Slider of music" |
| ⏮ ⏯ ⏭ | `Session.TrySkipPrevious/TogglePlayPause/SkipNext` | "Play/Pause", "Next/Previous" |

- Artwork falls back to an accent gradient + music glyph when a session exposes none.
- Transport buttons disable themselves when the session reports the control is unavailable
  (`PlaybackInfo.Controls.Is*Enabled`).
- **v1 progress is a read-only indicator** (scrub/seek is a fast follow — see Out of scope).

## Visual language (reuses existing tokens)

- Surface / stroke / glow, `TileBrush` / `FieldBrush`, text + divider brushes, radii — all from
  `Tokens.Dark.xaml` / `Tokens.Light.xaml`. No new palette.
- Accent `#5b7cfa` for the play button + progress fill + playing indicator; semantic colors reserved.
- Section headers are letter-spaced uppercase labels (same treatment as `SettingLabel`).
- Tabular numerals for all time values.

## Behavior & performance (hard rules, same as vitals)

- `IMediaController.Start()` is called **only when the Control Center opens**; `Stop()` on close
  (and on bar collapse). Stop unhooks every SMTC event and disposes the refresh timer → ~0 idle cost.
- The controller rebuilds full metadata (title/artist/artwork) **only on change events**; a 1 s timer
  refreshes just position/playback while open (cheap). Thumbnails are decoded only when they change.
- SMTC events arrive on background threads; the view model marshals to the UI thread (as BarViewModel
  already does for metric samples).

## Notifications section (scaffolded, not yet live)

The column is built and themed, but Windows has **no supported way for an unpackaged WPF app** to read
the system notification list. `Windows.UI.Notifications.Management.UserNotificationListener` requires a
packaged identity (MSIX) + the `userNotificationListener` capability + a user consent prompt. So v1
ships the section with a clear empty/unsupported state behind `NotificationsViewModel`, and the real
feed is tracked as a separate workstream (requires MSIX packaging — see architecture notes).

## Out of scope for v1 (fast follows)

1. **Seek / scrub** — `TryChangePlaybackPositionAsync`, slider drag + marshaling.
2. **Volume per session** — SMTC doesn't expose it; would need per-app audio session (WASAPI).
3. **Rich app-name/icon resolution** — map AUMID → display name + real app icon via the shell.
4. **Live notifications** — requires MSIX packaging (above).

## New/changed code (component-first; main window stays thin)

- `GlideBar.Core`: `MediaSessionInfo`, `MediaPlaybackState`, `IMediaController` (+ `FakeMediaController`).
- `GlideBar.Media` (new project): `SystemMediaController`, `AddGlideBarMedia()`.
- `GlideBar.App`:
  - `ViewModels/MediaViewModel.cs` (`MediaViewModel` + `MediaSessionViewModel`),
    `ViewModels/NotificationsViewModel.cs`.
  - `Views/NowPlayingCard.xaml`, `Views/ControlCenterPanel.xaml` (self-contained UserControls).
  - `BarViewModel`: `Media`, `Notifications`, `IsControlCenterOpen`, `ToggleControlCenterCommand`.
  - `BarWindow`: one toggle button (×2 orientations) + one `ControlCenterPopup`. Nothing else.
