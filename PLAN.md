# GlideBar — Project Plan

> **GlideBar** is a working name. A hover-activated desktop "hot bar" for Windows 11: find & open files,
> quick-launch pinned files/folders/apps, and glance at live system vitals (CPU, RAM, GPU, temperature) —
> all from a movable, minimal bar that stays out of the way until you need it.

**Status:** Planning / design-review phase (M0)
**Platform:** Windows 11 (also target Windows 10 20H2+)
**Date:** 2026-10-14

---

## 1. Vision & product summary

A PC equivalent of a phone's notification/control hot bar. It lives at a screen edge (or floats), stays
collapsed and invisible to the eye until the mouse approaches, then slides in with:

- **File search** — type to find files/folders/apps instantly, open or reveal them.
- **Pinned shortcuts** — pin frequently used files, folders and apps for one-click access without opening File Explorer.
- **System vitals** — live CPU %, RAM usage, GPU %, and temperatures (CPU/GPU) with a compact, glanceable layout.
- **Movable** — drag to any edge/corner on any monitor; remembers its position.
- **Quiet** — minimal CPU/GPU cost, runs in the background from the system tray, no disturbance.

Design language: **modern, minimalistic**. Think translucent/acrylic surface, soft rounded geometry,
tight typographic hierarchy, restrained accent color, semantic colors only for load/temperature states.

---

## 2. Feature list (mapped to the request)

| # | Requirement (user's words)                         | Feature |
|---|----------------------------------------------------|---------|
| 1 | "hot bar which gets active when i hover over it"   | Edge trigger + hover-to-reveal / mouse-leave-to-hide, with dwell + grace timers |
| 2 | "find the files or open the files"                 | Fast search box (Windows Search / Everything) + open & reveal actions |
| 3 | "quick access ... pin and open them"               | Pinned files/folders/apps; drag-drop to add; one-click open |
| 4 | "without opening the File explorer"                | In-bar results list + launcher; never spawns Explorer unless "Reveal" chosen |
| 5 | "can be move any where"                            | Drag to reposition; snap to edges/corners; multi-monitor; persisted |
| 6 | "task manager performances ... memory, ram, GPU, temperature" | Live metrics widget (CPU %, RAM, GPU %, CPU/GPU temp) |
| 7 | "mordern and minimalitic"                          | Acrylic/mica surface, rounded, minimal, dark + light theme |
| 8 | "not heavy ... CPU and GPU ... run in background"  | Perf-counter-first metrics, polling paused when hidden, tray-resident, no busy loops |
| 9 | "PC Notification bar similar to Mobile"             | Collapsed sliver → expands into a control/notification-style panel |
| 10 | "see the music playing apps and what music it is playing" | **Now Playing (Control Center):** a flyout listing every media app (Spotify, browser, local) with artwork, title/artist, progress and play/pause · next/previous, via the Windows System Media Transport Controls. Includes a notifications section (scaffolded). |

---

## 3. Tech stack decision

**Recommendation: WPF on .NET 8 (C#).**

| Option | Verdict | Reasoning |
|--------|---------|-----------|
| **Electron** | ❌ | Chromium per-app = 150–400 MB RAM + constant GPU compositing. Directly conflicts with the "low CPU/GPU, runs quietly in background" requirement. |
| **Tauri (Rust + WebView2)** | ⚠️ | Light shell, but Windows hardware sensors (GPU/CPU temps) have no mature Rust path — you end up bundling a .NET/C sensor backend anyway. More glue, more moving parts. |
| **WinUI 3 / Windows App SDK** | ⚠️ | Most modern native look (Mica, Fluent). Viable, but packaging/deployment and window-management quirks add friction for a tray/overlay tool. Good fallback. |
| **WPF + .NET 8** | ✅ | Mature, native, low enough footprint (~60–90 MB), first-class topmost/transparent/drag windows, rich ecosystem for exactly our needs: **LibreHardwareMonitor** (sensors), **H.NotifyIcon** (tray), DWM acrylic/blur. Fastest path to a polished minimal overlay. |

**Chosen libraries**
- `LibreHardwareMonitorLib` — CPU/GPU/board temperatures & loads (optional, for the temperature feature).
- `System.Diagnostics.PerformanceCounter` + Win32 (`GlobalMemoryStatusEx`, `GetSystemTimes`) — cheap always-on CPU %, RAM, GPU % without elevation.
- `H.NotifyIcon.Wpf` — system tray icon + context menu.
- `CommunityToolkit.Mvvm` — lightweight MVVM (source-generated, no runtime bloat).
- (Optional) **Everything SDK** (voidtools) — instant file search when the user has Everything installed.

> If the team prefers a web-UI skin later, the backend services (sections 4.2–4.4) are UI-agnostic and
> could be reused behind a Tauri/WinUI shell with minimal change.

---

## 4. Architecture

Clean module boundaries so work parallelizes without code conflicts. Each box below is its own .NET project.

```
GlideBar.sln
├─ GlideBar.Core            // models + interfaces + DI + events (the contracts everyone depends on)
├─ GlideBar.HardwareMonitor // IMetricsProvider: perf counters (cheap) + LibreHardwareMonitor (temps)
├─ GlideBar.Search          // ISearchProvider: WindowsSearch / Everything; ILauncher: open/reveal/pin-launch
├─ GlideBar.Storage         // IStorage: JSON settings + pinned items in %AppData%\GlideBar
└─ GlideBar.App  (WPF)      // shell: hover/trigger window, tray, hotkey, drag; MVVM views; theme (chosen design)
```

**Data flow (one direction):** `App (ViewModels)` → consumes → `Core` interfaces, implemented by the three
service projects. Services never reference the App. UI depends on contracts, not implementations (DI-injected).

### 4.1 Core contracts (defined first, before parallel work starts)
- Models: `MetricSample`, `MetricKind`, `PinnedItem`, `SearchResult`, `AppSettings`, `BarPosition`.
- Interfaces: `IMetricsProvider`, `ISearchProvider`, `ILauncher`, `IStorage`, `ISettingsService`.
- `IHostedService`-style start/stop + an event/observable for metric ticks.

### 4.2 HardwareMonitor
- **Tier A (always on, no admin, cheap):** CPU % via `GetSystemTimes`; RAM via `GlobalMemoryStatusEx`;
  GPU % + GPU memory via `GPU Engine` / `GPU Adapter Memory` performance counters.
- **Tier B (opt-in, temps):** `LibreHardwareMonitorLib`. GPU temps (NVIDIA/AMD) often read without admin;
  **CPU core temperatures require elevation** (kernel driver). Degrade gracefully + offer "Run as admin for temps".
- Polling **paused whenever the bar is hidden**; default 1 s interval when visible (configurable).

### 4.3 Search & Launcher
- **Tier 1:** Everything (voidtools) IPC if installed — instant, near-zero cost.
- **Tier 2 (default):** Windows Search index via `Search.CollatorDSO` OLE DB query — uses the OS indexer, no custom always-on index of our own.
- **Tier 3:** direct enumeration for pinned/recent scopes only.
- Launcher: open (ShellExecute), reveal in Explorer, open containing folder, launch pinned app (incl. `shell:AppsFolder` for UWP).

### 4.4 Storage
- `%AppData%\GlideBar\config.json` — pinned items, bar position/edge, theme, poll interval, enabled sensors, autostart.
- Atomic writes (temp file + replace); schema-versioned; debounced saves.

### 4.5 App shell (window + interaction)
- Transparent, topmost, no-taskbar overlay window + a thin **trigger** region at the active edge.
- Hover-reveal with dwell-in / grace-out timers; slide/opacity transition (respects reduced-motion).
- Draggable, edge/corner snap, multi-monitor aware; global hotkey to summon (e.g. `Ctrl`+`Space`); tray icon; opt-in autostart.
- MVVM; views bind to the injected services; the **theme implements the design the user picks** from the mockups.

---

## 5. Performance budget (the "don't be heavy" contract)

- **Collapsed/idle:** no metric polling, no animation loops; only a lightweight edge-trigger watcher. Target ≈ 0 % CPU.
- **Visible:** perf counters are microsecond-cheap; 1 s poll. No per-frame work beyond short transitions.
- **GPU:** avoid continuous blur repaint; WPF idle compositing is negligible. Ship a "reduce effects" toggle.
- **Memory target:** < ~90 MB working set; lazy-load the sensor library only when temps are enabled.
- **Background:** single process, tray-resident, hidden window costs nothing meaningful.

---

## 6. Permissions & distribution notes

- **Admin / UAC:** only needed for CPU-core temperatures. App runs as standard user by default and relaunches
  elevated on demand (or ships a manifest option). All other features need no elevation.
- **SmartScreen:** an unsigned build triggers a warning on first run; code-signing is a later/release concern.
- **Antivirus:** sensor kernel driver (via LibreHardwareMonitor) can draw AV attention — document it.

---

## 7. Milestones

- **M0 — Plan + 10+ design mockups.** ← you are here (review & pick a design).
- **M1 — Scaffolding + Core contracts + DI wiring.**
- **M2 — Foundation services in parallel:** HardwareMonitor, Search/Launcher, Storage (with smoke tests).
- **M3 — App shell:** hover/trigger window, drag/snap, tray, hotkey (skeleton UI).
- **M4 — UI theme:** implement the chosen design; wire views to services.
- **M5 — Settings panel, autostart, perf pass, polish.**
- **M6 — Packaging:** self-contained single-file exe (and/or MSIX); optional signing.

---

## 8. Open questions (for you)

1. **App name** — keep *GlideBar*, or something else?
2. **Design** — which of the 10+ mockups (one or a blend)?
3. **CPU temperatures** — OK to require an optional admin/elevation step for those? (Everything else needs no admin.)
4. **File search** — is "Everything" installed, or should we rely on Windows Search by default?
5. **Packaging** — portable single exe, or an installer/MSIX with autostart baked in?

See `AGENTS.md` for how the build work is split across parallel agents, and `docs/architecture.md` for deeper technical notes.
