# Decision log

## D1 — Tech stack: WPF on .NET 10 (C#)  — 2026-10-14
**Decision:** Build GlideBar as a **WPF** app on **.NET 10** (LTS), using `LibreHardwareMonitorLib` for
temperatures/loads, `H.NotifyIcon.Wpf` for the tray, `CommunityToolkit.Mvvm` for MVVM.

**Why:**
- Hardware temperature/load reading has effectively one SDK on Windows — **LibreHardwareMonitorLib** (.NET).
  Rust's `sysinfo` can't read GPUs, so Tauri/native-Rust would require bundling LHM as a sidecar anyway → more complexity, and Rust isn't installed.
- **.NET 10 + Windows Desktop runtime is already installed** (`10.0.401`) → no setup; latest LTS.
- WPF has the lowest-complexity, best-documented path for the hard overlay parts (topmost, click-through, per-monitor-V2 DPI, global hotkey, tray). Performance comes from architecture (polling pauses when hidden) + ReadyToRun/trimming, not NativeAOT (WPF can't use it yet).

**Alternative kept in reserve:** WinUI 3 — more modern Fluent look, but higher packaging/overlay-window complexity.

## D2 — Design: Adaptive Morph Bar  — 2026-10-14
**Decision:** Hybrid of concept **01 Top Notch Pill** + **02 Edge Dock**. Floating horizontal pill that is
draggable anywhere and **auto-transforms to a vertical dock when snapped to a left/right edge**. Full spec in
[`design-spec.md`](design-spec.md).

## D3 — Target framework per project
- `GlideBar.Core`, `GlideBar.Storage` → `net10.0` (platform-neutral)
- `GlideBar.HardwareMonitor`, `GlideBar.Search` → `net10.0-windows`
- `GlideBar.App` → `net10.0-windows`, `UseWPF=true`
