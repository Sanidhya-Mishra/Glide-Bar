# GlideBar

A hover-activated desktop **hot bar** for Windows 11 — find & open files, quick-launch pinned
files/folders/apps, and watch live system vitals (CPU, RAM, GPU, temperature), from a movable,
minimal bar that stays hidden until your mouse approaches. Think of it as a PC version of a phone's
notification/control hot bar.

> **GlideBar** is a working name — easy to change.

## Updates

**Latest: [v1.1.0](https://github.com/Sanidhya-Mishra/Glide-Bar/releases/latest)** — Pinned tiles now
show each app/file's real Windows icon (no more generic placeholder), and shortcut (`.lnk`) icons
render crisply without the shortcut-arrow overlay.

📦 **[Download the latest release](https://github.com/Sanidhya-Mishra/Glide-Bar/releases/latest)**
(one-click installer or portable `.exe`) · 📋 [All releases](https://github.com/Sanidhya-Mishra/Glide-Bar/releases)
· 📝 Full history in [`CHANGELOG.md`](CHANGELOG.md).

## Highlights
- 🔍 Instant file / folder / app search → open or reveal
- 📌 Pin shortcuts for one-click access (no File Explorer needed)
- 📊 Live CPU %, RAM, GPU %, and CPU/GPU temperature
- 🖱️ Reveals on hover; drag it to any edge or corner, on any monitor
- 🪶 Low CPU/GPU footprint; tray-resident; polling pauses when hidden
- 🎨 Modern, minimal; dark + light themes

## Project docs
- [`PLAN.md`](PLAN.md) — full plan, tech-stack decision, features, milestones
- [`AGENTS.md`](AGENTS.md) — how the build is split across parallel agents
- [`docs/architecture.md`](docs/architecture.md) — technical architecture notes
- [`designs/`](designs/) — design mockups (10+ concepts to choose from)

## Tech stack (proposed)
WPF on .NET 8 (C#) · LibreHardwareMonitor (sensors) · H.NotifyIcon (tray) · CommunityToolkit.Mvvm ·
Windows Search / Everything (file search).

## Build & run
Requires the **.NET 10 SDK** (already installed). From the repo root:

```powershell
dotnet build "src\GlideBar.slnx" -c Debug
dotnet run --project "src\GlideBar.App"
```

It's a **tray-resident** app (no taskbar button). Once running:
- The bar lives near the **top-center** of your primary monitor — **hover** it to expand, move the mouse away to collapse.
- **Drag** it anywhere; drop it near a **left/right edge** and it morphs into a vertical dock; near top/bottom it stays a horizontal pill.
- **Ctrl+Space** summons/dismisses it; the **tray icon** has Show/Hide · Pause · Settings · Start with Windows · Quit.
- Temperatures are **opt-in** (CPU temp may need running elevated); CPU/RAM/GPU load work without admin.

Settings, pinned items, position and theme persist to `%AppData%\GlideBar\config.json`.

## Status
**M4 complete — integrated build runs.** Solution builds clean (0 warnings); the app launches on the real
HardwareMonitor, Search, and Storage services. Remaining polish (M5): a settings flyout, pin add/remove/reorder
drag-and-drop UI, and a richer vertical-dock search field. See [docs/decisions.md](docs/decisions.md) and
[docs/design-spec.md](docs/design-spec.md).
